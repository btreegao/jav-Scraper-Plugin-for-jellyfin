using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ProjectVersionTests
{
    [Fact]
    public void Project_version_is_0_1_1()
    {
        var project = XDocument.Parse(ReadProjectFile());
        var propertyGroup = Assert.Single(project.Descendants("PropertyGroup"));

        Assert.Equal("0.1.1", (string?)propertyGroup.Element("Version"));
        Assert.Equal("0.1.1.0", (string?)propertyGroup.Element("AssemblyVersion"));
        Assert.Equal("0.1.1.0", (string?)propertyGroup.Element("FileVersion"));
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
