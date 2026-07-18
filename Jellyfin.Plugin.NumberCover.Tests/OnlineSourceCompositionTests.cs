using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class OnlineSourceCompositionTests
{
    [Fact]
    public async Task Shared_cache_keeps_jable_not_found_separate_from_dmm_success()
    {
        var cache = new CoverLookupCache();
        var jableCalls = 0;
        var dmmCalls = 0;
        var jable = new JableCoverLookupService(
            (_, _) =>
            {
                jableCalls++;
                return Task.FromResult("<meta property='og:image' content='https://example.test/cover.jpg'>");
            },
            maxRequestsPerMinute: 60,
            timeoutSeconds: 10,
            cache: cache);
        var dmm = new DmmCoverLookupService(
            (_, _) =>
            {
                dmmCalls++;
                return Task.FromResult(CreateImageResponse());
            },
            maxRequestsPerMinute: 60,
            timeoutSeconds: 10,
            cache: cache);

        var jableFirst = await jable.LookupAsync("IPZZ-034", CancellationToken.None);
        var dmmFirst = await dmm.LookupAsync("IPZZ-034", CancellationToken.None);
        var jableRepeat = await jable.LookupAsync(" ipzz-034 ", CancellationToken.None);
        var dmmRepeat = await dmm.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.NotFound, jableFirst.Status);
        Assert.Equal(CoverLookupStatus.Success, dmmFirst.Status);
        Assert.Equal(CoverLookupStatus.NotFound, jableRepeat.Status);
        Assert.Equal(CoverLookupStatus.Success, dmmRepeat.Status);
        Assert.Equal(1, jableCalls);
        Assert.Equal(1, dmmCalls);
    }

    [Fact]
    public async Task Shared_rate_limiter_blocks_dmm_until_the_jable_request_leaves_the_window()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var rateLimiter = new SlidingWindowRateLimiter(1, timeProvider);
        var jableCalls = 0;
        var dmmCalls = 0;
        var jable = new JableCoverLookupService(
            (_, _) =>
            {
                jableCalls++;
                return Task.FromResult("<meta property='og:image' content='https://jable.tv/covers/abc-123.jpg'>");
            },
            maxRequestsPerMinute: 1,
            timeoutSeconds: 10,
            rateLimiter: rateLimiter);
        var dmm = new DmmCoverLookupService(
            (_, _) =>
            {
                dmmCalls++;
                return Task.FromResult(CreateImageResponse());
            },
            maxRequestsPerMinute: 1,
            timeoutSeconds: 10,
            rateLimiter: rateLimiter);

        await jable.LookupAsync("ABC-123", CancellationToken.None);
        var dmmLookup = dmm.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(1, jableCalls);
        Assert.Equal(0, dmmCalls);
        Assert.False(dmmLookup.IsCompleted);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var result = await dmmLookup;

        Assert.Equal(CoverLookupStatus.Success, result.Status);
        Assert.Equal(1, dmmCalls);
    }

    [Fact]
    public async Task Jable_cache_hit_propagates_cancellation_without_fetching_again()
    {
        var fetchCalls = 0;
        var jable = new JableCoverLookupService(
            (_, _) =>
            {
                fetchCalls++;
                return Task.FromResult("<meta property='og:image' content='https://jable.tv/covers/ipzz-034.jpg'>");
            },
            maxRequestsPerMinute: 60,
            timeoutSeconds: 10);

        var seeded = await jable.LookupAsync("IPZZ-034", CancellationToken.None);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            jable.LookupAsync(" ipzz-034 ", cancellationSource.Token));

        Assert.Equal(CoverLookupStatus.Success, seeded.Status);
        Assert.Equal(1, fetchCalls);
    }

    [Fact]
    public async Task Jable_cancellation_after_a_token_ignoring_fetch_does_not_cache_the_result()
    {
        using var cancellationSource = new CancellationTokenSource();
        var fetchCalls = 0;
        var jable = new JableCoverLookupService(
            (_, _) =>
            {
                fetchCalls++;
                if (fetchCalls == 1)
                {
                    cancellationSource.Cancel();
                }

                return Task.FromResult("<meta property='og:image' content='https://jable.tv/covers/ipzz-034.jpg'>");
            },
            maxRequestsPerMinute: 60,
            timeoutSeconds: 10);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            jable.LookupAsync("IPZZ-034", cancellationSource.Token));

        var retry = await jable.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, retry.Status);
        Assert.Equal(2, fetchCalls);
    }

    private static HttpResponseMessage CreateImageResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return response;
    }
}
