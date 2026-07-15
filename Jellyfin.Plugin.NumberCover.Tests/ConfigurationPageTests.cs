using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ConfigurationPageTests
{
    [Fact]
    public void Page_uses_the_embedded_external_controller()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");

        Assert.Contains("data-controller=\"__plugin/NumberCoverConfigJs\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Jellyfin_form_components_are_declared()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");

        Assert.Contains("data-require=\"emby-input,emby-button,emby-checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"checkboxContainer checkboxContainer-withDescription\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\" is=\"emby-checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"number\" is=\"emby-input\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Plugin_registers_the_controller_resource()
    {
        var pluginSource = ReadPluginFile("Plugin.cs");

        Assert.Contains("Name = \"NumberCoverConfigJs\"", pluginSource, StringComparison.Ordinal);
        Assert.Contains("Configuration.configPage.js", pluginSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_embeds_the_controller_resource()
    {
        var projectSource = ReadPluginFile("Jellyfin.Plugin.NumberCover.csproj");

        Assert.Contains("<EmbeddedResource Include=\"Configuration\\configPage.js\" />", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_is_page_scoped_and_loads_and_saves_configuration()
    {
        var controllerSource = ReadPluginFile("Configuration", "configPage.js");

        Assert.Contains("export default function NumberCoverConfigController(page)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("page.querySelector('#EnableJableSource')", controllerSource, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewshow', loadConfiguration)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.getPluginConfiguration(pluginId)", controllerSource, StringComparison.Ordinal);
        Assert.Contains("apiClient.updatePluginConfiguration(pluginId, config)", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("document.getElementById(", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector(", controllerSource, StringComparison.Ordinal);
    }

    private static string ReadPluginFile(params string[] relativeParts)
    {
        var pathParts = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Jellyfin.Plugin.NumberCover",
        }.Concat(relativeParts).ToArray();
        var path = Path.GetFullPath(Path.Combine(pathParts));

        Assert.True(File.Exists(path), $"Plugin file not found at {path}");
        return File.ReadAllText(path);
    }
}
