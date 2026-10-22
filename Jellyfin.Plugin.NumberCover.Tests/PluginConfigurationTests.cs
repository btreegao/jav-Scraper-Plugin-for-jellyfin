using Jellyfin.Plugin.NumberCover.Configuration;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_enable_online_sources_and_cover_replacement_with_rate_limiting()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableJableSource);
        Assert.False(configuration.OnlyFillMissing);
        Assert.Equal(12, configuration.MaxRequestsPerMinute);
        Assert.Equal(10, configuration.TimeoutSeconds);
    }
}
