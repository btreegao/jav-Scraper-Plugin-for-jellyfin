using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NumberCover.Services;

public static partial class DmmImageUri
{
    private const string Host = "pics.dmm.co.jp";
    private const string PathPrefix = "/digital/video/";

    public static Uri Build(string number)
    {
        var parsed = NumberParser.Parse(number);
        if (parsed is null || !string.Equals(parsed, number, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The value must be a standalone normalized identifier.", nameof(number));
        }

        var separator = parsed.IndexOf('-');
        var prefix = parsed[..separator].ToLowerInvariant();
        var numericText = parsed[(separator + 1)..].PadLeft(5, '0');
        var contentId = prefix + numericText;

        return new Uri($"https://{Host}{PathPrefix}{contentId}/{contentId}pl.jpg", UriKind.Absolute);
    }

    public static bool IsAllowed(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (!path.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainingPath = path[PathPrefix.Length..];
        var separator = remainingPath.IndexOf('/');
        if (separator <= 0)
        {
            return false;
        }

        var contentId = remainingPath[..separator];
        return ContentIdRegex().IsMatch(contentId) &&
            path.Equals($"{PathPrefix}{contentId}/{contentId}pl.jpg", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\A[a-z]{2,8}[0-9]{5,6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContentIdRegex();
}
