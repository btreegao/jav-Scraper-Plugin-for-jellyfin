using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NumberCover.Services;

public static class NumberParser
{
    private static readonly Regex NumberPattern = new(
        @"\b(?<prefix>[A-Za-z]{2,8})[-_\s](?<number>[0-9]{2,6})(?:[Cc])?(?!-[0-9])(?=\b|_[A-Za-z])",
        RegexOptions.CultureInvariant);

    public static string? Parse(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        var normalizedInput = input.Normalize(NormalizationForm.FormKC);
        var match = NumberPattern.Match(normalizedInput);

        return match.Success
            ? $"{match.Groups["prefix"].Value.ToUpperInvariant()}-{match.Groups["number"].Value}"
            : null;
    }
}
