using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.NumberCover.Providers;

public sealed class JableImageProvider : IRemoteImageProvider, IHasOrder
{
    private static readonly HttpClient RemoteHttpClient = CreateHttpClient();

    private readonly CoverLookupCoordinator _lookupCoordinator;

    public JableImageProvider()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var timeProvider = TimeProvider.System;
        var cache = new CoverLookupCache(timeProvider);
        var rateLimiter = new SlidingWindowRateLimiter(configuration.MaxRequestsPerMinute, timeProvider);
        var dmmLookupService = new DmmCoverLookupService(
            FetchDmmHeaderAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds,
            cache,
            timeProvider,
            rateLimiter);
        var javDbLookupService = new JavDbCoverLookupService(
            FetchCatalogPageAsync, FetchCatalogImageAsync,
            configuration.MaxRequestsPerMinute, configuration.TimeoutSeconds, cache, timeProvider, rateLimiter);
        var r18LookupService = new R18CoverLookupService(
            FetchCatalogPageAsync, FetchCatalogImageAsync,
            configuration.MaxRequestsPerMinute, configuration.TimeoutSeconds, cache, timeProvider, rateLimiter);
        _lookupCoordinator = new CoverLookupCoordinator(
            r18LookupService.LookupAsync, javDbLookupService.LookupAsync, dmmLookupService.LookupAsync);
    }

    // Jellyfin stores this name in library fetcher settings; keep the legacy identifier.
    public string Name => "Number Cover (Jable)";

    public int Order => 0;

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        item is Movie ? [ImageType.Primary] : [];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || item is not Movie)
        {
            return [];
        }

        if (!CoverLookupPolicy.ShouldLookup(
                configuration.EnableJableSource,
                configuration.OnlyFillMissing,
                item.HasImage(ImageType.Primary)))
        {
            return [];
        }

        var number = NumberParser.Parse(item.Name) ?? NumberParser.Parse(item.Path);
        if (number is null)
        {
            return [];
        }

        var candidate = await _lookupCoordinator.LookupAsync(number, cancellationToken).ConfigureAwait(false);
        return candidate is not null
            ?
            [
                new RemoteImageInfo
                {
                    ProviderName = candidate.Source switch
                    {
                        CoverSource.Dmm => "Number Cover (DMM fallback)",
                        CoverSource.JavDb => "Number Cover (JavDB)",
                        CoverSource.R18 => "Number Cover (R18.dev)",
                        _ => Name,
                    },
                    Type = ImageType.Primary,
                    Url = candidate.CoverUri.AbsoluteUri,
                },
            ]
            : [];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !RemoteCoverUriValidator.IsAllowed(uri))
        {
            throw new ArgumentException("The URL is not an allowed cover image URL.", nameof(url));
        }

        return RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private Task<HttpResponseMessage> FetchDmmHeaderAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!DmmImageUri.IsAllowed(uri))
        {
            throw new ArgumentException("Only canonical DMM image URLs are allowed.", nameof(uri));
        }

        return RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NumberCover/0.2.1 (+https://github.com/btreegao/jav-Scraper-Plugin-for-jellyfin)");
        return client;
    }

    private static async Task<string> FetchCatalogPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !((uri.Host == "javdb.com" && uri.AbsolutePath == "/search") ||
              (uri.Host == "r18.dev" && uri.AbsolutePath.StartsWith("/videos/vod/movies/detail/-/dvd_id=", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/json", StringComparison.Ordinal))))
            throw new ArgumentException("Only catalog lookup URLs are allowed.", nameof(uri));

        using var response = await RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        const int maximumBytes = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes) throw new HttpRequestException("Catalog response is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > maximumBytes) throw new HttpRequestException("Catalog response is too large.");
            buffer.Write(chunk, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static Task<HttpResponseMessage> FetchCatalogImageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!JavDbCoverLookupService.IsImageUriAllowed(uri) && !R18CoverLookupService.IsImageUriAllowed(uri))
            throw new ArgumentException("Only catalog cover URLs are allowed.", nameof(uri));
        return RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
