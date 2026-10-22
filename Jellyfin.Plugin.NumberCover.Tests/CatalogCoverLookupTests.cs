using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class CatalogCoverLookupTests
{
    private const string JavDbImage = "https://c0.jdbstatic.com/covers/j2/J2WzAW.jpg";
    private const string R18Image = "https://pics.dmm.co.jp/mono/movie/adult/ipzz034/ipzz034pl.jpg";
    private const string R18Json = """{"content_id":"ipzz034","images":{"jacket_image":{"large":" ","large2":"https://pics.dmm.co.jp/mono/movie/adult/ipzz034/ipzz034pl.jpg"}}}""";

    [Fact]
    public void Lookup_urls_use_normalized_identifiers_and_reject_arbitrary_urls()
    {
        Assert.Equal("https://javdb.com/search?q=IPZZ-034&f=all", JavDbCoverLookupService.BuildSearchUri(" ipzz-034 ").AbsoluteUri);
        Assert.Equal("https://r18.dev/videos/vod/movies/detail/-/dvd_id=IPZZ-034/json", R18CoverLookupService.BuildApiUri("ipzz-034").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => JavDbCoverLookupService.BuildSearchUri("https://example.test/"));
        Assert.Throws<ArgumentException>(() => R18CoverLookupService.BuildApiUri("IPZZ-034&other=1"));
    }

    [Fact]
    public void JavDb_selects_the_exact_identifier_instead_of_the_first_fuzzy_result()
    {
        var html = Card("IPZZ-304", "https://c0.jdbstatic.com/covers/96/96DzVX.jpg") + Card("IPZZ-034", JavDbImage);
        Assert.Equal(new Uri(JavDbImage), Assert.Single(JavDbCoverLookupService.ExtractImageUris(html, "IPZZ-034")));
        Assert.Empty(JavDbCoverLookupService.ExtractImageUris(html, "IPZZ-03"));
    }

    [Fact]
    public void JavDb_ignores_comments_scripts_and_external_images()
    {
        var card = Card("IPZZ-034", JavDbImage);
        Assert.Empty(JavDbCoverLookupService.ExtractImageUris("<!--" + card + "--><script>" + card + "</script>" + Card("IPZZ-034", "https://example.test/cover.jpg"), "IPZZ-034"));
    }

    [Fact]
    public void R18_uses_large2_when_large_is_blank_and_rejects_mismatched_ids()
    {
        Assert.Equal(new Uri(R18Image), Assert.Single(R18CoverLookupService.ExtractImageUris(R18Json, "IPZZ-034")));
        Assert.Empty(R18CoverLookupService.ExtractImageUris(R18Json.Replace("\"content_id\":\"ipzz034\"", "\"dvd_id\":\"IPZZ-304\""), "IPZZ-034"));
        Assert.Empty(R18CoverLookupService.ExtractImageUris("null", "IPZZ-034"));
        Assert.Empty(R18CoverLookupService.ExtractImageUris("{}", "IPZZ-034"));
    }

    [Theory]
    [InlineData("https://c0.jdbstatic.com/covers/j2/J2WzAW.jpg", true)]
    [InlineData("https://jdbstatic.com/covers/j2/J2WzAW.jpg", true)]
    [InlineData("https://c0.jdbstatic.com.evil.test/covers/j2/J2WzAW.jpg", false)]
    [InlineData("https://user@c0.jdbstatic.com/covers/j2/J2WzAW.jpg", false)]
    [InlineData("https://c0.jdbstatic.com:444/covers/j2/J2WzAW.jpg", false)]
    [InlineData("http://c0.jdbstatic.com/covers/j2/J2WzAW.jpg", false)]
    [InlineData("https://c0.jdbstatic.com/covers/j2/J2WzAW.jpg?url=other", false)]
    [InlineData("https://c0.jdbstatic.com/avatars/j2/J2WzAW.jpg", false)]
    public void JavDb_image_allowlist_is_restricted(string url, bool expected) =>
        Assert.Equal(expected, JavDbCoverLookupService.IsImageUriAllowed(new Uri(url)));

    [Theory]
    [InlineData(R18Image, true)]
    [InlineData("https://pics.dmm.co.jp/digital/video/118abp00123/118abp00123pl.jpg", true)]
    [InlineData("https://pics.dmm.co.jp/mono/movie/adult/h_123abc123/h_123abc123pl.jpg", true)]
    [InlineData("https://pics.dmm.co.jp.evil.test/mono/movie/adult/ipzz034/ipzz034pl.jpg", false)]
    [InlineData("https://pics.dmm.co.jp/mono/movie/adult/ipzz034/otherpl.jpg", false)]
    [InlineData("https://pics.dmm.co.jp/mono/movie/adult/ipzz034/ipzz034pl.jpg?x=1", false)]
    [InlineData("https://pics.dmm.co.jp:444/mono/movie/adult/ipzz034/ipzz034pl.jpg", false)]
    [InlineData("https://user@pics.dmm.co.jp/mono/movie/adult/ipzz034/ipzz034pl.jpg", false)]
    [InlineData("https://pics.dmm.co.jp/other/ipzz034/ipzz034pl.jpg", false)]
    public void R18_image_allowlist_is_restricted(string url, bool expected) =>
        Assert.Equal(expected, R18CoverLookupService.IsImageUriAllowed(new Uri(url)));

    [Theory]
    [InlineData(200, "image/jpeg", CoverLookupStatus.Success)]
    [InlineData(200, "text/html", CoverLookupStatus.NotFound)]
    [InlineData(302, "image/jpeg", CoverLookupStatus.NotFound)]
    [InlineData(404, "image/jpeg", CoverLookupStatus.NotFound)]
    [InlineData(503, "text/html", CoverLookupStatus.TemporaryFailure)]
    public async Task Catalog_returns_only_verified_images(int status, string contentType, CoverLookupStatus expected)
    {
        foreach (var useR18 in new[] { false, true })
        {
            CatalogCoverLookupService service = useR18
                ? new R18CoverLookupService((_, _) => Task.FromResult(R18Json), (_, _) => Task.FromResult(Response(status, contentType)), 60, 10)
                : new JavDbCoverLookupService((_, _) => Task.FromResult(Card("IPZZ-034", JavDbImage)), (_, _) => Task.FromResult(Response(status, contentType)), 60, 10);
            Assert.Equal(expected, (await service.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        }
    }

    [Fact]
    public async Task R18_tries_the_second_image_after_a_broken_first_image()
    {
        var calls = 0;
        var json = R18Json.Replace("\"large\":\" \"", "\"large\":\"https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg\"");
        var service = new R18CoverLookupService((_, _) => Task.FromResult(json), (_, _) => Task.FromResult(Response(++calls == 1 ? 404 : 200, "image/jpeg")), 60, 10);
        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        Assert.Equal(new Uri(R18Image), result.CoverUri);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Shared_cache_keeps_catalog_sources_separate_and_reuses_case_insensitive_results()
    {
        var cache = new CoverLookupCache();
        var calls = 0;
        var javDb = new JavDbCoverLookupService((_, _) => { calls++; return Task.FromResult(Card("IPZZ-034", JavDbImage)); }, (_, _) => Task.FromResult(Response(200, "image/jpeg")), 60, 10, cache);
        var r18 = new R18CoverLookupService((_, _) => { calls++; return Task.FromResult(R18Json); }, (_, _) => Task.FromResult(Response(200, "image/jpeg")), 60, 10, cache);
        Assert.Equal(new Uri(JavDbImage), (await javDb.LookupAsync("IPZZ-034", CancellationToken.None)).CoverUri);
        Assert.Equal(new Uri(R18Image), (await r18.LookupAsync("IPZZ-034", CancellationToken.None)).CoverUri);
        await javDb.LookupAsync(" ipzz-034 ", CancellationToken.None);
        await r18.LookupAsync("ipzz-034", CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cancellation_after_fetch_is_propagated_without_caching_or_probing()
    {
        using var cts = new CancellationTokenSource();
        var fetches = 0;
        var probes = 0;
        var service = new R18CoverLookupService((_, _) => { if (++fetches == 1) cts.Cancel(); return Task.FromResult(R18Json); }, (_, _) => { probes++; return Task.FromResult(Response(200, "image/jpeg")); }, 60, 10);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LookupAsync("IPZZ-034", cts.Token));
        Assert.Equal(0, probes);
        Assert.Equal(CoverLookupStatus.Success, (await service.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task Catalog_metadata_and_image_probe_share_the_limiter()
    {
        var time = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, time);
        var probes = 0;
        var service = new R18CoverLookupService((_, _) => Task.FromResult(R18Json), (_, _) => { probes++; return Task.FromResult(Response(200, "image/jpeg")); }, 1, 10, rateLimiter: limiter);
        var lookup = service.LookupAsync("IPZZ-034", CancellationToken.None);
        Assert.False(lookup.IsCompleted);
        Assert.Equal(0, probes);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CoverLookupStatus.Success, (await lookup).Status);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task Malformed_json_and_transport_failure_do_not_escape_the_service()
    {
        var malformed = new R18CoverLookupService((_, _) => Task.FromResult("<html>challenge</html>"), (_, _) => throw new Exception("Must not probe"), 60, 10);
        Assert.Equal(CoverLookupStatus.TemporaryFailure, (await malformed.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        var unavailable = new JavDbCoverLookupService((_, _) => throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden), (_, _) => throw new Exception("Must not probe"), 60, 10);
        Assert.Equal(CoverLookupStatus.TemporaryFailure, (await unavailable.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
    }

    private static string Card(string number, string image) => $"<a class='box' href='/v/J2WzAW'><div class='cover'><img loading='lazy' src='{image}' /></div><div class='video-title'><strong>{number}</strong> Title</div></a>";
    private static HttpResponseMessage Response(int status, string contentType)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }
}
