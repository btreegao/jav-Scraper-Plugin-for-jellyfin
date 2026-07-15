using Jellyfin.Plugin.NumberCover.Domain;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class CoverLookupResultTests
{
    [Fact]
    public void Success_creates_a_result_with_an_absolute_https_cover_uri()
    {
        var coverUri = new Uri("https://images.example.com/cover.jpg");

        var result = CoverLookupResult.Success(coverUri);

        Assert.Equal(CoverLookupStatus.Success, result.Status);
        Assert.Equal(coverUri, result.CoverUri);
    }

    [Theory]
    [InlineData("http://images.example.com/cover.jpg")]
    [InlineData("file:///tmp/cover.jpg")]
    [InlineData("/covers/cover.jpg")]
    public void Success_rejects_uris_that_are_not_absolute_https(string uri)
    {
        Assert.Throws<ArgumentException>(() => CoverLookupResult.Success(new Uri(uri, UriKind.RelativeOrAbsolute)));
    }

    [Fact]
    public void Success_rejects_a_null_uri()
    {
        Assert.Throws<ArgumentNullException>(() => CoverLookupResult.Success(null!));
    }

    [Fact]
    public void NotFound_has_no_cover_uri()
    {
        var result = CoverLookupResult.NotFound();

        Assert.Equal(CoverLookupStatus.NotFound, result.Status);
        Assert.Null(result.CoverUri);
    }

    [Fact]
    public void TemporaryFailure_has_no_cover_uri()
    {
        var result = CoverLookupResult.TemporaryFailure();

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
        Assert.Null(result.CoverUri);
    }
}
