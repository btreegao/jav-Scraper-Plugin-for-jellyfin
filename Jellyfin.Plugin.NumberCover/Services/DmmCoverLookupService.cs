using System.Diagnostics;
using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class DmmCoverLookupService
{
    private const string CacheSource = "dmm:";

    private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> _fetchImageAsync;
    private readonly CoverLookupCache _cache;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly TimeSpan _timeout;

    public DmmCoverLookupService(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImageAsync,
        int maxRequestsPerMinute,
        int timeoutSeconds,
        CoverLookupCache? cache = null,
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _fetchImageAsync = fetchImageAsync ?? throw new ArgumentNullException(nameof(fetchImageAsync));
        _cache = cache ?? new CoverLookupCache(timeProvider);
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(maxRequestsPerMinute, timeProvider);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
    }

    public async Task<CoverLookupResult> LookupAsync(string number, CancellationToken callerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var cacheKey = CacheSource + number;
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return cached!;
        }

        try
        {
            await _rateLimiter.WaitAsync(callerToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            timeoutSource.CancelAfter(_timeout);
            var candidate = DmmImageUri.Build(number);

            using var response = await _fetchImageAsync(candidate, timeoutSource.Token).ConfigureAwait(false);
            var result = ClassifyResponse(response, candidate);
            _cache.Set(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CacheTemporaryFailure(cacheKey, "timeout");
        }
        catch
        {
            return CacheTemporaryFailure(cacheKey, "transport");
        }
    }

    private static CoverLookupResult ClassifyResponse(HttpResponseMessage response, Uri candidate)
    {
        var status = (int)response.StatusCode;
        Trace.WriteLine($"DMM cover lookup source=dmm identifier={Sanitize(candidate)} status={status}");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var mediaType = response.Content?.Headers.ContentType?.MediaType;
            return mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                ? CoverLookupResult.Success(candidate)
                : CoverLookupResult.NotFound();
        }

        return (status is >= 300 and <= 399) || response.StatusCode == HttpStatusCode.NotFound
            ? CoverLookupResult.NotFound()
            : CoverLookupResult.TemporaryFailure();
    }

    private CoverLookupResult CacheTemporaryFailure(string cacheKey, string status)
    {
        Trace.WriteLine($"DMM cover lookup source=dmm identifier={Sanitize(cacheKey[CacheSource.Length..])} status={status}");
        var result = CoverLookupResult.TemporaryFailure();
        _cache.Set(cacheKey, result);
        return result;
    }

    private static string Sanitize(Uri uri) => Sanitize(uri.AbsolutePath);

    private static string Sanitize(string value) => value.Replace('\r', '_').Replace('\n', '_');
}
