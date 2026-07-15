using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class OpenGraphImageExtractorTests
{
    [Theory]
    [InlineData("<meta property=\"og:image\" content=\"https://images.example.com/cover.jpg\">", "https://images.example.com/cover.jpg")]
    [InlineData("<meta content='https://images.example.com/cover.jpg' name='OG:IMAGE'>", "https://images.example.com/cover.jpg")]
    [InlineData("<META DATA-SOURCE=\"page\" CONTENT=\"https://images.example.com/cover.jpg\" PROPERTY=\"Og:ImAgE\">", "https://images.example.com/cover.jpg")]
    public void TryExtract_returns_an_absolute_https_uri_for_an_open_graph_image_meta_tag(string html, string expected)
    {
        var result = OpenGraphImageExtractor.TryExtract(html);

        Assert.Equal(new Uri(expected), result);
    }

    [Fact]
    public void TryExtract_uses_the_first_open_graph_image_meta_tag()
    {
        const string html = "<meta property=\"og:image\" content=\"https://images.example.com/first.jpg\"><meta name=\"og:image\" content=\"https://images.example.com/second.jpg\">";

        var result = OpenGraphImageExtractor.TryExtract(html);

        Assert.Equal(new Uri("https://images.example.com/first.jpg"), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<meta property=\"og:title\" content=\"A title\">")]
    [InlineData("<meta property=\"og:image\">")]
    [InlineData("<meta property=\"og:image\" content=\"\">")]
    [InlineData("<meta property=\"og:image\" content=\"http://images.example.com/cover.jpg\">")]
    [InlineData("<meta property=\"og:image\" content=\"file:///tmp/cover.jpg\">")]
    [InlineData("<meta property=\"og:image\" content=\"/covers/cover.jpg\">")]
    public void TryExtract_returns_null_when_the_first_open_graph_image_value_is_missing_or_not_absolute_https(string html)
    {
        Assert.Null(OpenGraphImageExtractor.TryExtract(html));
    }

    [Fact]
    public void TryExtract_does_not_fall_through_when_the_first_open_graph_image_is_invalid()
    {
        const string html = "<meta name=\"og:image\" content=\"http://images.example.com/insecure.jpg\"><meta property=\"og:image\" content=\"https://images.example.com/valid.jpg\">";

        Assert.Null(OpenGraphImageExtractor.TryExtract(html));
    }

    [Theory]
    [InlineData("<meta-annotation property=\"og:image\" content=\"https://images.example.com/not-a-meta-tag.jpg\"><meta property=\"og:image\" content=\"https://images.example.com/cover.jpg\">")]
    [InlineData("<!-- <meta property=\"og:image\" content=\"https://images.example.com/comment.jpg\"> --><meta property=\"og:image\" content=\"https://images.example.com/cover.jpg\">")]
    [InlineData("<script>const markup = '<meta property=\"og:image\" content=\"https://images.example.com/script.jpg\">';</script><meta property=\"og:image\" content=\"https://images.example.com/cover.jpg\">")]
    [InlineData("<style>.cover::before { content: '<meta property=\"og:image\" content=\"https://images.example.com/style.jpg\">'; }</style><meta property=\"og:image\" content=\"https://images.example.com/cover.jpg\">")]
    public void TryExtract_ignores_non_meta_tags_and_markup_text(string html)
    {
        var result = OpenGraphImageExtractor.TryExtract(html);

        Assert.Equal(new Uri("https://images.example.com/cover.jpg"), result);
    }

    [Theory]
    [InlineData("<script/>const markup = '<meta property=\"og:image\" content=\"https://images.example.com/script.jpg\">';")]
    [InlineData("<style/>.cover::before { content: '<meta property=\"og:image\" content=\"https://images.example.com/style.jpg\">'; }")]
    public void TryExtract_does_not_extract_meta_tags_from_self_closed_raw_text_elements(string html)
    {
        Assert.Null(OpenGraphImageExtractor.TryExtract(html));
    }

    [Fact]
    public void TryExtract_does_not_allow_a_later_property_value_to_replace_the_first_one()
    {
        const string html = "<meta property=\"og:title\" property=\"og:image\" content=\"https://images.example.com/property.jpg\">";

        Assert.Null(OpenGraphImageExtractor.TryExtract(html));
    }

    [Fact]
    public void TryExtract_does_not_allow_a_later_content_value_to_replace_the_first_one()
    {
        const string html = "<meta property=\"og:image\" content=\"http://images.example.com/first-content.jpg\" content=\"https://images.example.com/later-content.jpg\">";

        Assert.Null(OpenGraphImageExtractor.TryExtract(html));
    }
}
