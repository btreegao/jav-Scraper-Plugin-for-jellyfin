using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class CoverLookupCacheTests
{
    [Theory]
    [InlineData(CoverLookupStatus.Success, 30)]
    [InlineData(CoverLookupStatus.NotFound, 7)]
    [InlineData(CoverLookupStatus.TemporaryFailure, 1)]
    public void TryGet_returns_a_miss_after_the_status_specific_ttl_expires(CoverLookupStatus status, int ttl)
    {
        var timeProvider = new CacheTestTimeProvider();
        var cache = new CoverLookupCache(timeProvider);
        var expected = CreateResult(status);

        cache.Set("ABC-123", expected);

        Assert.True(cache.TryGet("ABC-123", out var actual));
        Assert.Same(expected, actual);

        timeProvider.Advance(status == CoverLookupStatus.TemporaryFailure
            ? TimeSpan.FromHours(ttl)
            : TimeSpan.FromDays(ttl));

        Assert.False(cache.TryGet("ABC-123", out actual));
        Assert.Null(actual);
    }

    [Fact]
    public void TryGet_uses_a_trimmed_case_insensitive_number_key()
    {
        var cache = new CoverLookupCache(new CacheTestTimeProvider());
        var expected = CoverLookupResult.Success(new Uri("https://images.example.com/cover.jpg"));

        cache.Set("  abc-123  ", expected);

        Assert.True(cache.TryGet("AbC-123", out var actual));
        Assert.Same(expected, actual);
    }

    [Fact]
    public void Set_removes_all_expired_entries()
    {
        var timeProvider = new CacheTestTimeProvider();
        var cache = new CoverLookupCache(timeProvider);

        cache.Set("expired", CoverLookupResult.TemporaryFailure());
        timeProvider.Advance(TimeSpan.FromHours(1));

        cache.Set("current", CoverLookupResult.NotFound());

        Assert.Equal(1, GetEntryCount(cache));
        Assert.True(cache.TryGet("current", out _));
    }

    [Fact]
    public void Set_evicts_the_entry_with_the_earliest_expiration_at_capacity()
    {
        var cache = new CoverLookupCache(new CacheTestTimeProvider(), maxEntries: 2);
        var success = CoverLookupResult.Success(new Uri("https://images.example.com/success.jpg"));
        var temporaryFailure = CoverLookupResult.TemporaryFailure();
        var notFound = CoverLookupResult.NotFound();

        cache.Set("success", success);
        cache.Set("temporary", temporaryFailure);
        cache.Set("not-found", notFound);

        Assert.True(cache.TryGet("success", out var cachedSuccess));
        Assert.Same(success, cachedSuccess);
        Assert.False(cache.TryGet("temporary", out _));
        Assert.True(cache.TryGet("not-found", out var cachedNotFound));
        Assert.Same(notFound, cachedNotFound);
    }

    private static int GetEntryCount(CoverLookupCache cache)
    {
        var entries = Assert.IsAssignableFrom<System.Collections.IDictionary>(
            typeof(CoverLookupCache)
                .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(cache));

        return entries.Count;
    }

    private static CoverLookupResult CreateResult(CoverLookupStatus status) => status switch
    {
        CoverLookupStatus.Success => CoverLookupResult.Success(new Uri("https://images.example.com/cover.jpg")),
        CoverLookupStatus.NotFound => CoverLookupResult.NotFound(),
        CoverLookupStatus.TemporaryFailure => CoverLookupResult.TemporaryFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

internal sealed class CacheTestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow += duration;
}
