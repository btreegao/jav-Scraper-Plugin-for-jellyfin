using System.Diagnostics;
using System.Net;
using System.Text;
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
        TrackingHttpContent? content = null;
        var service = CreateService((uri, _) =>
        {
            calls++;
            Assert.Equal("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", uri.AbsoluteUri);
            content = new TrackingHttpContent();
            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "image/jpeg", content));
        });

        var first = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        var second = await service.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, first.Status);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", first.CoverUri?.AbsoluteUri);
        Assert.Equal(CoverLookupStatus.Success, second.Status);
        Assert.Equal(1, calls);
        Assert.True(content?.IsDisposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, "image/jpeg")]
    [InlineData(HttpStatusCode.NotFound, "image/jpeg")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task LookupAsync_returns_not_found_for_non_image_candidates(HttpStatusCode status, string contentType)
    {
        TrackingHttpContent? content = null;
        var service = CreateService((_, _) =>
        {
            content = new TrackingHttpContent();
            return Task.FromResult(CreateResponse(status, contentType, content));
        });

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.NotFound, result.Status);
        Assert.True(content?.IsDisposed);
    }

    [Fact]
    public async Task LookupAsync_returns_temporary_failure_for_service_unavailable()
    {
        var calls = 0;
        var service = CreateService((_, _) =>
        {
            calls++;
            return Task.FromResult(CreateResponse(HttpStatusCode.ServiceUnavailable, "image/jpeg"));
        });

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        var cached = await service.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
        Assert.Equal(CoverLookupStatus.TemporaryFailure, cached.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LookupAsync_returns_temporary_failure_for_internal_cancellation()
    {
        var calls = 0;
        var service = CreateService((_, _) =>
        {
            calls++;
            throw new OperationCanceledException("internal timeout");
        });

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        var cached = await service.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
        Assert.Equal(CoverLookupStatus.TemporaryFailure, cached.Status);
        Assert.Equal(1, calls);

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        using var listener = new TextWriterTraceListener(writer);
        Trace.Listeners.Add(listener);
        try
        {
            var loggingService = CreateService((_, _) => throw new InvalidOperationException("must not fetch"));
            var invalid = "https://user:secret@example.test/\r\nIPZZ-034";

            await loggingService.LookupAsync(invalid, CancellationToken.None);
            listener.Flush();

            var log = output.ToString();
            Assert.Contains("<invalid>", log);
            Assert.DoesNotContain("secret", log);
            Assert.DoesNotContain("https://user:", log);
            Assert.DoesNotContain("secret\r\nIPZZ-034", log);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public async Task LookupAsync_propagates_cancellation_before_and_after_fetch_without_caching()
    {
        var seededCache = new CoverLookupCache();
        seededCache.Set("dmm:IPZZ-034", CoverLookupResult.Success(new Uri("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")));
        var seededService = CreateService((_, _) => throw new InvalidOperationException("must not fetch"), seededCache);
        using var cachedCancellation = new CancellationTokenSource();
        cachedCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            seededService.LookupAsync("IPZZ-034", cachedCancellation.Token));

        var fetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        TrackingHttpContent? content = null;
        var service = CreateService(async (_, _) =>
        {
            calls++;
            fetchStarted.TrySetResult(true);
            await releaseFetch.Task;
            content = new TrackingHttpContent();
            return CreateResponse(HttpStatusCode.OK, "image/jpeg", content);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LookupAsync("IPZZ-034", cancellation.Token));
        Assert.Equal(0, calls);

        using var fetchCancellation = new CancellationTokenSource();

        var cancelledLookup = service.LookupAsync("IPZZ-034", fetchCancellation.Token);
        await fetchStarted.Task;
        fetchCancellation.Cancel();
        releaseFetch.SetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledLookup);
        Assert.True(content?.IsDisposed);

        var retry = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, retry.Status);
        Assert.Equal(2, calls);
    }

    private static DmmCoverLookupService CreateService(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImageAsync,
        CoverLookupCache? cache = null) =>
        new(fetchImageAsync, maxRequestsPerMinute: 60, timeoutSeconds: 10, cache: cache);

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, string contentType, HttpContent? content = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = content ?? new ByteArrayContent([]),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private sealed class TrackingHttpContent : ByteArrayContent
    {
        public TrackingHttpContent()
            : base([])
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
