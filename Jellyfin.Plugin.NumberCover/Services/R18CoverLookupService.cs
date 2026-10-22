using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class R18CoverLookupService : CatalogCoverLookupService
{
    public R18CoverLookupService(
        Func<Uri, CancellationToken, Task<string>> fetchPage,
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImage,
        int maxRequestsPerMinute, int timeoutSeconds,
        CoverLookupCache? cache = null, TimeProvider? timeProvider = null, SlidingWindowRateLimiter? rateLimiter = null)
        : base("r18", fetchPage, fetchImage, maxRequestsPerMinute, timeoutSeconds, cache, timeProvider, rateLimiter) { }

    public static Uri BuildApiUri(string number)
    {
        var normalized = NumberParser.Parse(number);
        if (normalized is null || !string.Equals(normalized, number.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A standalone normalized identifier is required.", nameof(number));
        return new Uri("https://r18.dev/videos/vod/movies/detail/-/dvd_id=" + Uri.EscapeDataString(normalized) + "/json");
    }

    public static bool IsImageUriAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.Host.Equals("pics.dmm.co.jp", StringComparison.OrdinalIgnoreCase)) return false;
        return Regex.IsMatch(uri.AbsolutePath,
            @"\A/(?:digital/video|mono/movie/adult)/(?<id>[a-z0-9_]{3,64})/\k<id>pl\.jpg\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public static IEnumerable<Uri> ExtractImageUris(string json, string number)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) yield break;
        if (root.TryGetProperty("dvd_id", out var dvdId) &&
            (dvdId.ValueKind != JsonValueKind.String || !string.Equals(dvdId.GetString(), number, StringComparison.OrdinalIgnoreCase))) yield break;
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object ||
            !images.TryGetProperty("jacket_image", out var jacket) || jacket.ValueKind != JsonValueKind.Object) yield break;
        foreach (var key in new[] { "large", "large2" })
        {
            if (jacket.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(value.GetString()?.Trim(), UriKind.Absolute, out var uri) && IsImageUriAllowed(uri)) yield return uri;
        }
    }

    protected override Uri BuildLookupUri(string number) => BuildApiUri(number);
    protected override IEnumerable<Uri> ExtractCovers(string content, string number) => ExtractImageUris(content, number);
}
