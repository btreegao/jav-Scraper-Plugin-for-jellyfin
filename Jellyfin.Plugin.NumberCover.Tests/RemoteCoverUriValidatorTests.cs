using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public sealed class RemoteCoverUriValidatorTests
{
    [Theory]
    [InlineData("https://assets-cdn.jable.tv/covers/abc-123.jpg", true)]
    [InlineData("https://jable.tv/covers/abc-123.jpg", true)]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", true)]
    [InlineData("https://example.test/covers/abc-123.jpg", false)]
    [InlineData("https://pics.dmm.co.jp.example/digital/video/ipzz00034/ipzz00034pl.jpg", false)]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034.jpg", false)]
    [InlineData("http://jable.tv/covers/abc-123.jpg", false)]
    public void IsAllowed_accepts_only_jable_or_canonical_dmm_image_uris(string value, bool expected)
    {
        Assert.Equal(expected, RemoteCoverUriValidator.IsAllowed(new Uri(value)));
    }
}
