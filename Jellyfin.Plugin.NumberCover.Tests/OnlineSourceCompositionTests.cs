using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class OnlineSourceCompositionTests
{
    [Fact]
    public async Task Shared_cache_keeps_R18_miss_separate_from_Dmm_success()
    {
        var cache = new CoverLookupCache();
        var r18Calls = 0;
        var dmmCalls = 0;
        var r18 = new R18CoverLookupService(
            (_, _) => { r18Calls++; return Task.FromResult("{}"); },
            (_, _) => throw new Exception("No cover to probe"), 60, 10, cache);
        var dmm = new DmmCoverLookupService(
            (_, _) => { dmmCalls++; return Task.FromResult(CreateImageResponse()); }, 60, 10, cache);

        Assert.Equal(CoverLookupStatus.NotFound, (await r18.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        Assert.Equal(CoverLookupStatus.Success, (await dmm.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        Assert.Equal(CoverLookupStatus.NotFound, (await r18.LookupAsync(" ipzz-034 ", CancellationToken.None)).Status);
        Assert.Equal(CoverLookupStatus.Success, (await dmm.LookupAsync("ipzz-034", CancellationToken.None)).Status);
        Assert.Equal(1, r18Calls);
        Assert.Equal(1, dmmCalls);
    }

    [Fact]
    public async Task Shared_limiter_blocks_Dmm_until_R18_request_leaves_the_window()
    {
        var time = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, time);
        var dmmCalls = 0;
        var r18 = new R18CoverLookupService(
            (_, _) => Task.FromResult("{}"), (_, _) => throw new Exception("No cover to probe"), 1, 10, rateLimiter: limiter);
        var dmm = new DmmCoverLookupService(
            (_, _) => { dmmCalls++; return Task.FromResult(CreateImageResponse()); }, 1, 10, rateLimiter: limiter);

        await r18.LookupAsync("IPZZ-034", CancellationToken.None);
        var lookup = dmm.LookupAsync("IPZZ-034", CancellationToken.None);
        Assert.False(lookup.IsCompleted);
        Assert.Equal(0, dmmCalls);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CoverLookupStatus.Success, (await lookup).Status);
        Assert.Equal(1, dmmCalls);
    }

    private static HttpResponseMessage CreateImageResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return response;
    }
}
