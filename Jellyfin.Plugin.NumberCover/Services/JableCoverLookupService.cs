using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class JableCoverLookupService
{
    private const string CachePrefix = "jable:";

    private static readonly Uri DetailsBaseUri = new("https://jable.tv/videos/");

    private readonly Func<Uri, CancellationToken, Task<string>> _fetchPageAsync;
    private readonly CoverLookupCache _cache;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly TimeSpan _timeout;

    public JableCoverLookupService(
        Func<Uri, CancellationToken, Task<string>> fetchPageAsync,
        int maxRequestsPerMinute,
        int timeoutSeconds,
        CoverLookupCache? cache = null,
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _fetchPageAsync = fetchPageAsync ?? throw new ArgumentNullException(nameof(fetchPageAsync));
        _cache = cache ?? new CoverLookupCache(timeProvider);
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(maxRequestsPerMinute, timeProvider);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
    }

    public static Uri BuildDetailsPageUri(string number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var normalizedNumber = NumberParser.Parse(number);
        if (normalizedNumber is null
            || !string.Equals(normalizedNumber, number.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The number must be a standalone normalized identifier.", nameof(number));
        }

        return new Uri(DetailsBaseUri, Uri.EscapeDataString(normalizedNumber.ToLowerInvariant()) + "/");
    }

    public async Task<CoverLookupResult> LookupAsync(string number, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = CachePrefix + number.Trim();
        if (_cache.TryGet(cacheKey, out var cached))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return cached!;
        }

        try
        {
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            var html = await _fetchPageAsync(BuildDetailsPageUri(number), timeoutSource.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var coverUri = OpenGraphImageExtractor.TryExtract(html);
            var result = coverUri is not null && IsJableHost(coverUri)
                ? CoverLookupResult.Success(coverUri)
                : CoverLookupResult.NotFound();

            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var result = CoverLookupResult.TemporaryFailure();
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(cacheKey, result);
            return result;
        }
        catch
        {
            var result = CoverLookupResult.TemporaryFailure();
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(cacheKey, result);
            return result;
        }
    }

    public static bool IsJableHost(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(uri.Host, "jable.tv", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".jable.tv", StringComparison.OrdinalIgnoreCase))
        && string.IsNullOrEmpty(uri.UserInfo);
}
