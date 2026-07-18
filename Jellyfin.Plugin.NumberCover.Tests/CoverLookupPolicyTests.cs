using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class CoverLookupPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void ShouldLookup_applies_online_source_and_existing_cover_policy(
        bool onlineSourcesEnabled,
        bool onlyFillMissing,
        bool hasPrimaryImage,
        bool expected)
    {
        Assert.Equal(expected, CoverLookupPolicy.ShouldLookup(
            onlineSourcesEnabled,
            onlyFillMissing,
            hasPrimaryImage));
    }
}
