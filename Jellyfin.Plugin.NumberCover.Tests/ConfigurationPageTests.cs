using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ConfigurationPageTests
{
    private static readonly Regex CSharpCommentOrLiteral = new(
        """
        "(?:\\.|[^"\\])*"
        |
        '(?:\\.|[^'\\])*'
        |
        (?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)
        """,
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ExplicitIfFalseDirective = new(
        @"^\s*#\s*if\s+false\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex IfDirective = new(
        @"^\s*#\s*if\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex EndIfDirective = new(
        @"^\s*#\s*endif\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Page_uses_the_embedded_external_controller()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");
        var document = XDocument.Parse(html);
        var configurationPage = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute("id") == "NumberCoverConfigPage");

        Assert.Equal("__plugin/NumberCoverConfigJs", (string?)configurationPage.Attribute("data-controller"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Jellyfin_form_components_are_declared()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");
        var document = XDocument.Parse(html);
        var configurationPage = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute("id") == "NumberCoverConfigPage");

        Assert.Equal(
            "emby-input,emby-button,emby-checkbox",
            (string?)configurationPage.Attribute("data-require"));
        Assert.Contains(
            document.Descendants(),
            element => HasClass(element, "checkboxContainer")
                && HasClass(element, "checkboxContainer-withDescription"));

        AssertControl(document, "input", "EnableJableSource", "checkbox", "emby-checkbox");
        AssertControl(document, "input", "OnlyFillMissing", "checkbox", "emby-checkbox");
        AssertControl(document, "input", "MaxRequestsPerMinute", "number", "emby-input");
        AssertControl(document, "input", "TimeoutSeconds", "number", "emby-input");

        var submitButton = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "button"
                && (string?)element.Attribute("type") == "submit");
        Assert.Equal("emby-button", (string?)submitButton.Attribute("is"));
    }

    [Fact]
    public void Plugin_registers_the_controller_resource()
    {
        var pluginSource = ReadPluginFile("Plugin.cs");
        var controllerPagePattern = new Regex(
            """
            new\s+PluginPageInfo\s*
            \{\s*
            Name\s*=\s*"NumberCoverConfigJs",\s*
            EmbeddedResourcePath\s*=\s*\$"[^"\r\n]*Configuration\.configPage\.js",?\s*
            \}
            """,
            RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

        var intendedRegistration =
            """
            new PluginPageInfo
            {
                Name = "NumberCoverConfigJs",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.js",
            }
            """;
        var blockCommentedRegistration = $"/*{Environment.NewLine}{intendedRegistration}{Environment.NewLine}*/";
        var lineCommentedRegistration = $"// {intendedRegistration.ReplaceLineEndings(" ")}";
        var inactiveRegistration =
            $$"""
            #if false
            #if FEATURE_FLAG
            #endif
            {{intendedRegistration}}
            #endif
            """;

        Assert.Matches(controllerPagePattern, SanitizeActiveCSharpSource(intendedRegistration));
        Assert.DoesNotMatch(controllerPagePattern, SanitizeActiveCSharpSource(blockCommentedRegistration));
        Assert.DoesNotMatch(controllerPagePattern, SanitizeActiveCSharpSource(lineCommentedRegistration));
        Assert.DoesNotMatch(controllerPagePattern, SanitizeActiveCSharpSource(inactiveRegistration));
        Assert.Matches(controllerPagePattern, SanitizeActiveCSharpSource(pluginSource));
    }

    [Fact]
    public void Project_embeds_the_controller_resource()
    {
        var projectSource = ReadPluginFile("Jellyfin.Plugin.NumberCover.csproj");
        var project = XDocument.Parse(projectSource);

        Assert.Contains(
            project.Descendants().Where(element => element.Name.LocalName == "EmbeddedResource"),
            resource => ((string?)resource.Attribute("Include"))?.Replace('\\', '/')
                == "Configuration/configPage.js");
    }

    [Fact]
    public void Controller_is_page_scoped_and_loads_and_saves_configuration()
    {
        var controllerSource = ReadPluginFile("Configuration", "configPage.js");

        Assert.Contains("export default function NumberCoverConfigController(page)", controllerSource, StringComparison.Ordinal);
        Assert.All(
            new[]
            {
                "#NumberCoverConfigForm",
                "#NumberCoverConfigError",
                "#EnableJableSource",
                "#OnlyFillMissing",
                "#MaxRequestsPerMinute",
                "#TimeoutSeconds",
            },
            selector => Assert.Contains($"page.querySelector('{selector}')", controllerSource, StringComparison.Ordinal));
        Assert.Contains("page.addEventListener('viewshow', loadConfiguration)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.getPluginConfiguration(pluginId)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.updatePluginConfiguration(pluginId, config)", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bdocument\b", controllerSource);
    }

    private static void AssertControl(
        XDocument document,
        string elementName,
        string id,
        string type,
        string component)
    {
        var control = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == elementName
                && (string?)element.Attribute("id") == id);

        Assert.Equal(type, (string?)control.Attribute("type"));
        Assert.Equal(component, (string?)control.Attribute("is"));
    }

    private static bool HasClass(XElement element, string className)
    {
        var classes = ((string?)element.Attribute("class"))?.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return classes?.Contains(className, StringComparer.Ordinal) == true;
    }

    private static string SanitizeActiveCSharpSource(string source)
    {
        return RemoveExplicitIfFalseRegions(RemoveCSharpComments(source));
    }

    private static string RemoveCSharpComments(string source)
    {
        return CSharpCommentOrLiteral.Replace(
            source,
            match => match.Groups["comment"].Success
                ? string.Concat(match.Value.Where(character => character is '\r' or '\n'))
                : match.Value);
    }

    private static string RemoveExplicitIfFalseRegions(string source)
    {
        var result = new StringBuilder(source.Length);
        using var reader = new StringReader(source);
        var inactiveDepth = 0;

        while (reader.ReadLine() is { } line)
        {
            if (inactiveDepth > 0)
            {
                if (IfDirective.IsMatch(line))
                {
                    inactiveDepth++;
                }
                else if (EndIfDirective.IsMatch(line))
                {
                    inactiveDepth--;
                }

                continue;
            }

            if (ExplicitIfFalseDirective.IsMatch(line))
            {
                inactiveDepth = 1;
                continue;
            }

            result.AppendLine(line);
        }

        return result.ToString();
    }

    private static string ReadPluginFile(params string[] relativeParts)
    {
        var searchedPaths = new List<string>();

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var pluginSourceRoot = Path.Combine(directory.FullName, "Jellyfin.Plugin.NumberCover");
            var projectPath = Path.Combine(pluginSourceRoot, "Jellyfin.Plugin.NumberCover.csproj");
            searchedPaths.Add(projectPath);

            if (!File.Exists(projectPath))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(
                new[] { pluginSourceRoot }.Concat(relativeParts).ToArray()));

            Assert.True(File.Exists(path), $"Plugin file not found at {path}");
            return File.ReadAllText(path);
        }

        Assert.Fail(
            $"Plugin source root not found. Searched:{Environment.NewLine}{string.Join(Environment.NewLine, searchedPaths)}");
        return string.Empty;
    }
}
