using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class CoverLookupCache
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan TemporaryFailureTtl = TimeSpan.FromHours(1);

    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;

    public CoverLookupCache(TimeProvider? timeProvider = null, int maxEntries = 1024)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxEntries = maxEntries > 0
            ? maxEntries
            : throw new ArgumentOutOfRangeException(nameof(maxEntries));
    }

    public void Set(string number, CoverLookupResult result)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(result);

        var key = Normalize(number);

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            RemoveExpiredEntries(now);

            if (!_entries.ContainsKey(key) && _entries.Count == _maxEntries)
            {
                RemoveEarliestExpiringEntry();
            }

            var expiresAt = now + GetTtl(result.Status);
            _entries[key] = new CacheEntry(result, expiresAt);
        }
    }

    public bool TryGet(string number, out CoverLookupResult? result)
    {
        ArgumentNullException.ThrowIfNull(number);

        var key = Normalize(number);

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            RemoveExpiredEntries(now);

            if (_entries.TryGetValue(key, out var entry))
            {
                result = entry.Result;
                return true;
            }
        }

        result = null;
        return false;
    }

    private static string Normalize(string number) => number.Trim().ToUpperInvariant();

    private static TimeSpan GetTtl(CoverLookupStatus status) => status switch
    {
        CoverLookupStatus.Success => SuccessTtl,
        CoverLookupStatus.NotFound => NotFoundTtl,
        CoverLookupStatus.TemporaryFailure => TemporaryFailureTtl,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private void RemoveExpiredEntries(DateTimeOffset now)
    {
        foreach (var key in _entries
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _entries.Remove(key);
        }
    }

    private void RemoveEarliestExpiringEntry()
    {
        string? keyToRemove = null;
        CacheEntry? earliestEntry = null;

        foreach (var pair in _entries)
        {
            if (earliestEntry is null
                || pair.Value.ExpiresAt < earliestEntry.ExpiresAt
                || (pair.Value.ExpiresAt == earliestEntry.ExpiresAt
                    && StringComparer.Ordinal.Compare(pair.Key, keyToRemove) < 0))
            {
                keyToRemove = pair.Key;
                earliestEntry = pair.Value;
            }
        }

        _entries.Remove(keyToRemove!);
    }

    private sealed record CacheEntry(CoverLookupResult Result, DateTimeOffset ExpiresAt);
}
