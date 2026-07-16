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
        callerToken.ThrowIfCancellationRequested();

        var cacheKey = CacheSource + number;
        var safeIdentifier = GetSafeIdentifier(number);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            callerToken.ThrowIfCancellationRequested();
            return cached!;
        }

        try
        {
            await _rateLimiter.WaitAsync(callerToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            timeoutSource.CancelAfter(_timeout);
            var candidate = DmmImageUri.Build(number);

            using var response = await _fetchImageAsync(candidate, timeoutSource.Token).ConfigureAwait(false);
            callerToken.ThrowIfCancellationRequested();
            var result = ClassifyResponse(response, candidate, safeIdentifier);
            return CacheResult(cacheKey, result, callerToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CacheTemporaryFailure(cacheKey, safeIdentifier, "timeout", callerToken);
        }
        catch
        {
            return CacheTemporaryFailure(cacheKey, safeIdentifier, "transport", callerToken);
        }
    }

    private static CoverLookupResult ClassifyResponse(HttpResponseMessage response, Uri candidate, string safeIdentifier)
    {
        var status = (int)response.StatusCode;
        Trace.WriteLine($"DMM cover lookup source=dmm identifier={safeIdentifier} status={status}");

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

    private CoverLookupResult CacheResult(string cacheKey, CoverLookupResult result, CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        _cache.Set(cacheKey, result);
        callerToken.ThrowIfCancellationRequested();
        return result;
    }

    private CoverLookupResult CacheTemporaryFailure(
        string cacheKey,
        string safeIdentifier,
        string status,
        CancellationToken callerToken)
    {
        Trace.WriteLine($"DMM cover lookup source=dmm identifier={safeIdentifier} status={status}");
        return CacheResult(cacheKey, CoverLookupResult.TemporaryFailure(), callerToken);
    }

    private static string GetSafeIdentifier(string number)
    {
        var trimmed = number.Trim();
        var parsed = NumberParser.Parse(number);
        return parsed is not null && string.Equals(parsed, trimmed, StringComparison.OrdinalIgnoreCase)
            ? parsed
            : "<invalid>";
    }
}
