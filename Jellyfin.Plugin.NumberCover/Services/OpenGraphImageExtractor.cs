using System.Net;

namespace Jellyfin.Plugin.NumberCover.Services;

public static class OpenGraphImageExtractor
{
    public static Uri? TryExtract(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var position = 0;
        while (position < html.Length)
        {
            var tagStart = html.IndexOf('<', position);
            if (tagStart < 0)
            {
                break;
            }

            if (html.AsSpan(tagStart).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", tagStart + 4, StringComparison.Ordinal);
                position = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }

            if (!TryReadTag(html, tagStart, out var tag))
            {
                position = tagStart + 1;
                continue;
            }

            position = tag.End;

            if (!tag.IsClosingTag &&
                (string.Equals(tag.Name, "script", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tag.Name, "style", StringComparison.OrdinalIgnoreCase)))
            {
                position = SkipRawTextElement(html, position, tag.Name);
                continue;
            }

            if (tag.IsClosingTag || !string.Equals(tag.Name, "meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = ReadAttributes(html, tag.AttributesStart, tag.AttributesEnd);
            var isOpenGraphImage = IsOpenGraphImage(attributes, "property") || IsOpenGraphImage(attributes, "name");

            if (isOpenGraphImage)
            {
                attributes.TryGetValue("content", out var content);
                return TryCreateHttpsUri(content, out var uri) ? uri : null;
            }
        }

        return null;
    }

    private static int SkipRawTextElement(string html, int position, string tagName)
    {
        while (position < html.Length)
        {
            var tagStart = html.IndexOf('<', position);
            if (tagStart < 0)
            {
                return html.Length;
            }

            if (TryReadTag(html, tagStart, out var tag) &&
                tag.IsClosingTag &&
                string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase))
            {
                return tag.End;
            }

            position = tagStart + 1;
        }

        return html.Length;
    }

    private static bool TryReadTag(string html, int tagStart, out HtmlTag tag)
    {
        tag = default;

        var position = tagStart + 1;
        var isClosingTag = false;
        if (position < html.Length && html[position] == '/')
        {
            isClosingTag = true;
            position++;
        }

        if (position >= html.Length || !char.IsLetter(html[position]))
        {
            return false;
        }

        var nameStart = position;
        while (position < html.Length && !char.IsWhiteSpace(html[position]) && html[position] is not '/' and not '>')
        {
            position++;
        }

        var attributesStart = position;
        var quote = '\0';
        while (position < html.Length)
        {
            var character = html[position];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '>')
            {
                var attributesEnd = position;
                tag = new HtmlTag(
                    html[nameStart..attributesStart],
                    attributesStart,
                    attributesEnd,
                    position + 1,
                    isClosingTag,
                    IsSelfClosingTag(html, attributesStart, attributesEnd));
                return true;
            }

            position++;
        }

        return false;
    }

    private static bool IsSelfClosingTag(string html, int start, int end)
    {
        var position = end - 1;
        while (position >= start && char.IsWhiteSpace(html[position]))
        {
            position--;
        }

        return position >= start && html[position] == '/';
    }

    private static Dictionary<string, string?> ReadAttributes(string html, int start, int end)
    {
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var position = start;

        while (position < end)
        {
            while (position < end && char.IsWhiteSpace(html[position]))
            {
                position++;
            }

            if (position >= end || html[position] == '/')
            {
                position++;
                continue;
            }

            var nameStart = position;
            while (position < end && !char.IsWhiteSpace(html[position]) && html[position] is not '=' and not '/')
            {
                position++;
            }

            if (nameStart == position)
            {
                position++;
                continue;
            }

            var name = html[nameStart..position];
            while (position < end && char.IsWhiteSpace(html[position]))
            {
                position++;
            }

            string? value = null;
            if (position < end && html[position] == '=')
            {
                position++;
                while (position < end && char.IsWhiteSpace(html[position]))
                {
                    position++;
                }

                value = ReadAttributeValue(html, ref position, end);
            }

            attributes.TryAdd(name, value);
        }

        return attributes;
    }

    private static string? ReadAttributeValue(string html, ref int position, int end)
    {
        if (position >= end)
        {
            return string.Empty;
        }

        var quote = html[position];
        if (quote is '\'' or '"')
        {
            var valueStart = ++position;
            while (position < end && html[position] != quote)
            {
                position++;
            }

            var value = html[valueStart..position];
            if (position < end)
            {
                position++;
            }

            return WebUtility.HtmlDecode(value);
        }

        var unquotedValueStart = position;
        while (position < end && !char.IsWhiteSpace(html[position]))
        {
            position++;
        }

        return WebUtility.HtmlDecode(html[unquotedValueStart..position]);
    }

    private static bool IsOpenGraphImage(IReadOnlyDictionary<string, string?> attributes, string name)
    {
        return attributes.TryGetValue(name, out var value) &&
            string.Equals(value, "og:image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateHttpsUri(string? value, out Uri? uri)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.Host))
        {
            uri = candidate;
            return true;
        }

        uri = null;
        return false;
    }

    private readonly record struct HtmlTag(
        string Name,
        int AttributesStart,
        int AttributesEnd,
        int End,
        bool IsClosingTag,
        bool IsSelfClosingTag);
}
