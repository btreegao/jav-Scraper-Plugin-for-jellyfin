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
    private static readonly HttpClient JableHttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    });

    private readonly JableCoverLookupService _lookupService;

    public JableImageProvider()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        _lookupService = new JableCoverLookupService(
            FetchPageAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds);
    }

    public string Name => "Number Cover (Jable)";

    public int Order => 0;

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        item is Movie ? [ImageType.Primary] : [];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.EnableJableSource || item is not Movie)
        {
            return [];
        }

        if (configuration.OnlyFillMissing && item.HasImage(ImageType.Primary))
        {
            return [];
        }

        var number = NumberParser.Parse(item.Name) ?? NumberParser.Parse(item.Path);
        if (number is null)
        {
            return [];
        }

        var result = await _lookupService.LookupAsync(number, cancellationToken).ConfigureAwait(false);
        return result.Status == CoverLookupStatus.Success && result.CoverUri is not null
            ?
            [
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = result.CoverUri.AbsoluteUri,
                },
            ]
            : [];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !JableCoverLookupService.IsJableHost(uri))
        {
            throw new ArgumentException("Only Jable HTTPS image URLs are allowed.", nameof(url));
        }

        return JableHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> FetchPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!JableCoverLookupService.IsJableHost(uri))
        {
            throw new ArgumentException("Only Jable HTTPS detail pages are allowed.", nameof(uri));
        }

        using var response = await JableHttpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
