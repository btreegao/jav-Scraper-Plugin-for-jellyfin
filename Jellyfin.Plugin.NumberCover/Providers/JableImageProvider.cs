using System.Net.Http;
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
    private static readonly HttpClient RemoteHttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    });

    private readonly CoverLookupCoordinator _lookupCoordinator;

    public JableImageProvider()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var timeProvider = TimeProvider.System;
        var cache = new CoverLookupCache(timeProvider);
        var rateLimiter = new SlidingWindowRateLimiter(configuration.MaxRequestsPerMinute, timeProvider);
        var jableLookupService = new JableCoverLookupService(
            FetchPageAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds,
            cache,
            timeProvider,
            rateLimiter);
        var dmmLookupService = new DmmCoverLookupService(
            FetchDmmHeaderAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds,
            cache,
            timeProvider,
            rateLimiter);
        _lookupCoordinator = new CoverLookupCoordinator(jableLookupService.LookupAsync, dmmLookupService.LookupAsync);
    }

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
                    ProviderName = candidate.Source == CoverSource.Dmm ? "Number Cover (DMM fallback)" : Name,
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
            throw new ArgumentException("Only Jable HTTPS or canonical DMM image URLs are allowed.", nameof(url));
        }

        return RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> FetchPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!JableCoverLookupService.IsJableHost(uri))
        {
            throw new ArgumentException("Only Jable HTTPS detail pages are allowed.", nameof(uri));
        }

        using var response = await RemoteHttpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> FetchDmmHeaderAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!DmmImageUri.IsAllowed(uri))
        {
            throw new ArgumentException("Only canonical DMM image URLs are allowed.", nameof(uri));
        }

        return RemoteHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
