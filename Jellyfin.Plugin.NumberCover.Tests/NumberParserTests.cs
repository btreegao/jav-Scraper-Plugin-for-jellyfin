using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class NumberParserTests
{
    [Theory]
    [InlineData("[ABC-123] title", "ABC-123")]
    [InlineData("abc_001 sample", "ABC-001")]
    [InlineData("abc 001 sample", "ABC-001")]
    [InlineData("IPZZ-034C", "IPZZ-034")]
    [InlineData("ＡＢＣ－１２３", "ABC-123")]
    public void Parse_returns_a_canonical_number_for_a_bounded_supported_format(string input, string expected)
    {
        Assert.Equal(expected, NumberParser.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("title only")]
    [InlineData("1ABC-123")]
    [InlineData("A-123")]
    [InlineData("ABC-1")]
    [InlineData("ABC-1234567")]
    [InlineData("ABC-123X")]
    [InlineData("ABC-123-456")]
    [InlineData("ABC-123C-456")]
    public void Parse_returns_null_for_missing_or_non_bounded_formats(string? input)
    {
        Assert.Null(NumberParser.Parse(input));
    }
}
