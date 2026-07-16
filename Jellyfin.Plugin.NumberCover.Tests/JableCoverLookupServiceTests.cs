using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class JableCoverLookupServiceTests
{
    [Fact]
    public void BuildDetailsPageUri_normalizes_the_number_for_the_public_detail_page()
    {
        var uri = JableCoverLookupService.BuildDetailsPageUri(" ABC-123 ");

        Assert.Equal("https://jable.tv/videos/abc-123/", uri.AbsoluteUri);
    }

    [Fact]
    public void BuildDetailsPageUri_rejects_a_value_that_is_not_a_standalone_number()
    {
        Assert.Throws<ArgumentException>(() =>
            JableCoverLookupService.BuildDetailsPageUri("https://evil.example/"));
    }

    [Fact]
    public async Task LookupAsync_returns_a_same_site_https_open_graph_cover()
    {
        var service = CreateService(_ => "<meta property='og:image' content='https://jable.tv/covers/abc-123.jpg'>");

        var result = await service.LookupAsync("ABC-123", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, result.Status);
        Assert.Equal("https://jable.tv/covers/abc-123.jpg", result.CoverUri?.AbsoluteUri);
    }

    [Fact]
    public async Task LookupAsync_rejects_an_external_open_graph_cover_and_caches_the_miss()
    {
        var calls = 0;
        var service = CreateService(_ =>
        {
            calls++;
            return "<meta property='og:image' content='https://example.test/cover.jpg'>";
        });

        var first = await service.LookupAsync("ABC-123", CancellationToken.None);
        var second = await service.LookupAsync(" abc-123 ", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.NotFound, first.Status);
        Assert.Equal(CoverLookupStatus.NotFound, second.Status);
        Assert.Equal(1, calls);
    }

    private static JableCoverLookupService CreateService(Func<Uri, string> response) =>
        new(
            (uri, _) => Task.FromResult(response(uri)),
            maxRequestsPerMinute: 12,
            timeoutSeconds: 10);
}
