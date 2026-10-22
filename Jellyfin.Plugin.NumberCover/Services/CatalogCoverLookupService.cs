using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

// Shared request, cache and cancellation behavior for the catalog-backed sources.
public abstract class CatalogCoverLookupService
{
    private readonly string _source;
    private readonly Func<Uri, CancellationToken, Task<string>> _fetchPage;
    private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> _fetchImage;
    private readonly CoverLookupCache _cache;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly TimeSpan _timeout;

    protected CatalogCoverLookupService(
        string source,
        Func<Uri, CancellationToken, Task<string>> fetchPage,
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImage,
        int maxRequestsPerMinute,
        int timeoutSeconds,
        CoverLookupCache? cache = null,
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _source = source;
        _fetchPage = fetchPage ?? throw new ArgumentNullException(nameof(fetchPage));
        _fetchImage = fetchImage ?? throw new ArgumentNullException(nameof(fetchImage));
        _cache = cache ?? new CoverLookupCache(timeProvider);
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(maxRequestsPerMinute, timeProvider);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
    }

    protected abstract Uri BuildLookupUri(string number);
    protected abstract IEnumerable<Uri> ExtractCovers(string content, string number);

    public async Task<CoverLookupResult> LookupAsync(string number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NumberParser.Parse(number);
        if (normalized is null || !string.Equals(normalized, number.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A standalone normalized identifier is required.", nameof(number));
        }

        var cacheKey = _source + ":" + normalized;
        if (_cache.TryGet(cacheKey, out var cached))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return cached!;
        }

        CoverLookupResult result;
        try
        {
            var content = await RequestAsync(token => _fetchPage(BuildLookupUri(normalized), token), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            result = CoverLookupResult.NotFound();
            foreach (var uri in ExtractCovers(content, normalized).Distinct())
            {
                try
                {
                    using var response = await RequestAsync(token => _fetchImage(uri, token), cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (response.StatusCode == HttpStatusCode.OK &&
                        response.Content?.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        result = CoverLookupResult.Success(uri);
                        break;
                    }
                    if ((int)response.StatusCode is 429 or >= 500)
                    {
                        result = CoverLookupResult.TemporaryFailure();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    result = CoverLookupResult.TemporaryFailure();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            result = CoverLookupResult.NotFound();
        }
        catch
        {
            result = CoverLookupResult.TemporaryFailure();
        }

        cancellationToken.ThrowIfCancellationRequested();
        _cache.Set(cacheKey, result);
        return result;
    }

    private async Task<T> RequestAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken callerToken)
    {
        await _rateLimiter.WaitAsync(callerToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(_timeout);
        return await request(timeout.Token).ConfigureAwait(false);
    }
}
