using Jellyfin.Plugin.NumberCover.Configuration;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_are_safe_for_a_new_installation()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.EnableJableSource);
        Assert.True(configuration.OnlyFillMissing);
        Assert.Equal(12, configuration.MaxRequestsPerMinute);
        Assert.Equal(10, configuration.TimeoutSeconds);
    }
}
