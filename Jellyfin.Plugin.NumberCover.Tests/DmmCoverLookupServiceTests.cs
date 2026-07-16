using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class DmmCoverLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_accepts_an_image_and_caches_case_insensitive_repeat_lookups()
    {
        var calls = 0;
        var service = CreateService((uri, _) =>
        {
            calls++;
            Assert.Equal("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", uri.AbsoluteUri);
            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "image/jpeg"));
        });

        var first = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        var second = await service.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, first.Status);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", first.CoverUri?.AbsoluteUri);
        Assert.Equal(CoverLookupStatus.Success, second.Status);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, "image/jpeg")]
    [InlineData(HttpStatusCode.NotFound, "image/jpeg")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task LookupAsync_returns_not_found_for_non_image_candidates(HttpStatusCode status, string contentType)
    {
        var service = CreateService((_, _) => Task.FromResult(CreateResponse(status, contentType)));

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task LookupAsync_returns_temporary_failure_for_service_unavailable()
    {
        var service = CreateService((_, _) => Task.FromResult(CreateResponse(HttpStatusCode.ServiceUnavailable, "image/jpeg")));

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
    }

    [Fact]
    public async Task LookupAsync_returns_temporary_failure_for_internal_cancellation()
    {
        var service = CreateService((_, _) => throw new OperationCanceledException("internal timeout"));

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
    }

    [Fact]
    public async Task LookupAsync_propagates_pre_cancelled_caller_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var service = CreateService((_, _) =>
        {
            calls++;
            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "image/jpeg"));
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LookupAsync("IPZZ-034", cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task LookupAsync_propagates_cancellation_after_fetch_and_does_not_cache_the_response()
    {
        var fetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = CreateService(async (_, _) =>
        {
            calls++;
            fetchStarted.TrySetResult(true);
            await releaseFetch.Task;
            return CreateResponse(HttpStatusCode.OK, "image/jpeg");
        });
        using var cancellation = new CancellationTokenSource();

        var cancelledLookup = service.LookupAsync("IPZZ-034", cancellation.Token);
        await fetchStarted.Task;
        cancellation.Cancel();
        releaseFetch.SetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledLookup);

        var retry = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, retry.Status);
        Assert.Equal(2, calls);
    }

    private static DmmCoverLookupService CreateService(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImageAsync) =>
        new(fetchImageAsync, maxRequestsPerMinute: 60, timeoutSeconds: 10);

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, string contentType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }
}
