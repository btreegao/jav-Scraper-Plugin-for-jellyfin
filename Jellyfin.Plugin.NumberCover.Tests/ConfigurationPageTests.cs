using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ConfigurationPageTests
{
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
        LoadJellyfinCommonAssembly();
        var controllerPage = Assert.Single(
            GetPluginPages(),
            page => page.Name == "NumberCoverConfigJs");

        Assert.Equal(
            "Jellyfin.Plugin.NumberCover.Configuration.configPage.js",
            controllerPage.EmbeddedResourcePath);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static IEnumerable<MediaBrowser.Model.Plugins.PluginPageInfo> GetPluginPages()
        {
            var plugin = (Jellyfin.Plugin.NumberCover.Plugin)RuntimeHelpers.GetUninitializedObject(
                typeof(Jellyfin.Plugin.NumberCover.Plugin));
            return plugin.GetPages();
        }
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
        Assert.Contains("async function loadConfiguration()", controllerSource, StringComparison.Ordinal);
        Assert.Contains("form.addEventListener('submit', async function (event)", controllerSource, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(controllerSource, "finally {"));
        Assert.Equal(2, CountOccurrences(controllerSource, "dashboard.hideLoadingMsg();"));
        Assert.DoesNotContain(".then(function ()", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.getPluginConfiguration(pluginId)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.updatePluginConfiguration(pluginId, config)", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bdocument\b", controllerSource);
    }

    private static int CountOccurrences(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
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

    private static void LoadJellyfinCommonAssembly()
    {
        const string assemblyName = "MediaBrowser.Common";
        if (AppDomain.CurrentDomain.GetAssemblies().Any(
                assembly => assembly.GetName().Name == assemblyName))
        {
            return;
        }

        var relativePackagePath = Path.Combine(
            "jellyfin.common",
            "10.11.11",
            "lib",
            "net9.0",
            $"{assemblyName}.dll");
        var searchedPaths = new List<string>();

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".nuget", relativePackagePath);
            searchedPaths.Add(candidate);

            if (File.Exists(candidate))
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                return;
            }
        }

        var globalCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            relativePackagePath);
        searchedPaths.Add(globalCandidate);

        Assert.True(
            File.Exists(globalCandidate),
            $"Jellyfin common assembly not found. Searched:{Environment.NewLine}{string.Join(Environment.NewLine, searchedPaths)}");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(globalCandidate);
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
