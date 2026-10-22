using System.Net;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class JavDbCoverLookupService : CatalogCoverLookupService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    public JavDbCoverLookupService(
        Func<Uri, CancellationToken, Task<string>> fetchPage,
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImage,
        int maxRequestsPerMinute, int timeoutSeconds,
        CoverLookupCache? cache = null, TimeProvider? timeProvider = null, SlidingWindowRateLimiter? rateLimiter = null)
        : base("javdb", fetchPage, fetchImage, maxRequestsPerMinute, timeoutSeconds, cache, timeProvider, rateLimiter) { }

    public static Uri BuildSearchUri(string number)
    {
        var normalized = NumberParser.Parse(number);
        if (normalized is null || !string.Equals(normalized, number.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A standalone normalized identifier is required.", nameof(number));
        return new Uri("https://javdb.com/search?q=" + Uri.EscapeDataString(normalized) + "&f=all");
    }

    public static bool IsImageUriAllowed(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Host.Equals("jdbstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".jdbstatic.com", StringComparison.OrdinalIgnoreCase)) &&
        Regex.IsMatch(uri.AbsolutePath, @"\A/covers/[A-Za-z0-9/_-]+\.(?:jpg|jpeg|png|webp)\z", Options, RegexTimeout);

    public static IEnumerable<Uri> ExtractImageUris(string html, string number)
    {
        // Limit parsing to movie links and require the displayed identifier to match exactly.
        html = Regex.Replace(html, @"<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", "", Options, RegexTimeout);
        foreach (Match link in Regex.Matches(html, @"<a\b(?<attrs>[^>]*)>(?<body>.*?)</a\s*>", Options, RegexTimeout))
        {
            var href = Attribute(link.Groups["attrs"].Value, "href");
            if (href is null || !Regex.IsMatch(href, @"\A/v/[A-Za-z0-9]+\z", Options, RegexTimeout)) continue;
            var body = link.Groups["body"].Value;
            var title = Regex.Match(body, @"<div\b[^>]*class\s*=\s*['""][^'""]*\bvideo-title\b[^'""]*['""][^>]*>\s*<strong\b[^>]*>(?<id>[^<]*)</strong>", Options, RegexTimeout);
            if (!title.Success || !string.Equals(WebUtility.HtmlDecode(title.Groups["id"].Value).Trim(), number, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match image in Regex.Matches(body, @"<img\b(?<attrs>[^>]*)>", Options, RegexTimeout))
            {
                var source = Attribute(image.Groups["attrs"].Value, "src");
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && IsImageUriAllowed(uri)) yield return uri;
            }
        }
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, @"(?:\A|\s)" + name + @"\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", Options, RegexTimeout);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    protected override Uri BuildLookupUri(string number) => BuildSearchUri(number);
    protected override IEnumerable<Uri> ExtractCovers(string content, string number) => ExtractImageUris(content, number);
}
