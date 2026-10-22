using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class CoverLookupCoordinatorTests
{
    [Theory]
    [InlineData(0, false, "r18")]
    [InlineData(1, false, "r18,javdb")]
    [InlineData(2, false, "r18,javdb,dmm")]
    [InlineData(-1, false, "r18,javdb,dmm")]
    [InlineData(1, true, "r18,javdb")]
    [InlineData(2, true, "r18,javdb,dmm")]
    [InlineData(-1, true, "r18,javdb,dmm")]
    public async Task Lookup_uses_R18_then_JavDb_then_Dmm_and_stops_at_first_success(int successIndex, bool temporaryFailure, string expectedCalls)
    {
        var calls = new List<string>();
        var sources = new[] { CoverSource.R18, CoverSource.JavDb, CoverSource.Dmm };
        var uri = new Uri("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg");
        Task<CoverLookupResult> Lookup(int index, string name)
        {
            calls.Add(name);
            return Task.FromResult(index == successIndex ? CoverLookupResult.Success(uri)
                : temporaryFailure ? CoverLookupResult.TemporaryFailure() : CoverLookupResult.NotFound());
        }
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Lookup(0, "r18"), (_, _) => Lookup(1, "javdb"), (_, _) => Lookup(2, "dmm"));

        var result = await coordinator.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(expectedCalls, string.Join(',', calls));
        if (successIndex < 0) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(sources[successIndex], result.Source);
            Assert.Equal(uri, result.CoverUri);
        }
    }

    [Fact]
    public async Task Cancelled_lookup_does_not_call_any_source()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;
        Task<CoverLookupResult> Lookup(string _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(CoverLookupResult.NotFound());
        }
        var coordinator = new CoverLookupCoordinator(Lookup, Lookup, Lookup);
        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.LookupAsync("IPZZ-034", cts.Token));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Cancellation_after_any_source_is_propagated_and_stops_fallback(int cancelledIndex)
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        Task<CoverLookupResult> Lookup(int index)
        {
            calls++;
            if (index == cancelledIndex) cts.Cancel();
            return Task.FromResult(CoverLookupResult.NotFound());
        }
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Lookup(0), (_, _) => Lookup(1), (_, _) => Lookup(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.LookupAsync("IPZZ-034", cts.Token));
        Assert.Equal(cancelledIndex + 1, calls);
    }
}
