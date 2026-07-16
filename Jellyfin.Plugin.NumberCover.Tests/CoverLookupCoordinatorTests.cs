using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class CoverLookupCoordinatorTests
{
    [Fact]
    public async Task LookupAsync_returns_the_jable_candidate_without_calling_dmm_when_jable_succeeds()
    {
        var dmmCalls = 0;
        var jableUri = new Uri("https://jable.tv/covers/abc-123.jpg");
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(CoverLookupResult.Success(jableUri)),
            (_, _) =>
            {
                dmmCalls++;
                return Task.FromResult(CoverLookupResult.Success(new Uri("https://pics.dmm.co.jp/dmm.jpg")));
            });

        var result = await coordinator.LookupAsync("ABC-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(jableUri, result.CoverUri);
        Assert.Equal(CoverSource.Jable, result.Source);
        Assert.Equal(0, dmmCalls);
    }

    [Theory]
    [InlineData(CoverLookupStatus.NotFound)]
    [InlineData(CoverLookupStatus.TemporaryFailure)]
    public async Task LookupAsync_falls_back_to_dmm_when_jable_does_not_succeed(CoverLookupStatus jableStatus)
    {
        var dmmUri = new Uri("https://pics.dmm.co.jp/dmm.jpg");
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(CreateResult(jableStatus)),
            (_, _) => Task.FromResult(CoverLookupResult.Success(dmmUri)));

        var result = await coordinator.LookupAsync("ABC-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(dmmUri, result.CoverUri);
        Assert.Equal(CoverSource.Dmm, result.Source);
    }

    [Fact]
    public async Task LookupAsync_returns_null_when_neither_source_succeeds()
    {
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(CoverLookupResult.NotFound()),
            (_, _) => Task.FromResult(CoverLookupResult.TemporaryFailure()));

        var result = await coordinator.LookupAsync("ABC-123", CancellationToken.None);

        Assert.Null(result);
    }

    private static CoverLookupResult CreateResult(CoverLookupStatus status) => status switch
    {
        CoverLookupStatus.NotFound => CoverLookupResult.NotFound(),
        CoverLookupStatus.TemporaryFailure => CoverLookupResult.TemporaryFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
