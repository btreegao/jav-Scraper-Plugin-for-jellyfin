using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class DmmImageUriTests
{
    [Theory]
    [InlineData("IPZZ-034", "https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("ABC-12", "https://pics.dmm.co.jp/digital/video/abc00012/abc00012pl.jpg")]
    [InlineData("ABC-12345", "https://pics.dmm.co.jp/digital/video/abc12345/abc12345pl.jpg")]
    [InlineData("ABC-123456", "https://pics.dmm.co.jp/digital/video/abc123456/abc123456pl.jpg")]
    public void Build_maps_a_standalone_identifier_to_a_dmm_cover_uri(string number, string expected)
    {
        Assert.Equal(new Uri(expected), DmmImageUri.Build(number));
    }

    [Theory]
    [InlineData("https://example.com/IPZZ-034")]
    [InlineData("title IPZZ-034")]
    [InlineData("IPZZ-1")]
    public void Build_rejects_non_standalone_or_unsupported_identifiers(string number)
    {
        Assert.Throws<ArgumentException>(() => DmmImageUri.Build(number));
    }

    [Fact]
    public void IsAllowed_accepts_the_canonical_dmm_cover_uri()
    {
        Assert.True(DmmImageUri.IsAllowed(new Uri(
            "https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")));
    }

    [Theory]
    [InlineData("http://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp.example/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://user:p%40ss@pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp:444/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/movie/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/otherpl.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg?size=large")]
    public void IsAllowed_rejects_uris_outside_the_exact_dmm_cover_shape(string uri)
    {
        Assert.False(DmmImageUri.IsAllowed(new Uri(uri)));
    }
}
