using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ProjectVersionTests
{
    [Fact]
    public void Project_version_is_0_1_2()
    {
        var project = XDocument.Parse(ReadProjectFile());
        var version = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "Version");
        var assemblyVersion = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "AssemblyVersion");
        var fileVersion = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "FileVersion");

        Assert.Equal("0.1.2", version.Value);
        Assert.Equal("0.1.2.0", assemblyVersion.Value);
        Assert.Equal("0.1.2.0", fileVersion.Value);
    }

    private static string ReadProjectFile()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(
                directory.FullName,
                "Jellyfin.Plugin.NumberCover",
                "Jellyfin.Plugin.NumberCover.csproj");

            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        Assert.Fail("Plugin project file not found.");
        return string.Empty;
    }
}
