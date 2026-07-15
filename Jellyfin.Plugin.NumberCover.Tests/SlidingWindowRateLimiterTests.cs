using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class SlidingWindowRateLimiterTests
{
    [Fact]
    public void Constructor_rejects_a_non_positive_request_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(-1));
    }

    [Fact]
    public async Task WaitAsync_waits_until_the_oldest_request_leaves_the_one_minute_window()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(2, timeProvider);

        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);
        var thirdRequest = limiter.WaitAsync(CancellationToken.None);

        Assert.False(thirdRequest.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(59));
        Assert.False(thirdRequest.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await thirdRequest;
    }

    [Fact]
    public async Task WaitAsync_honors_cancellation_while_waiting_for_a_permit()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, timeProvider);
        using var cancellationSource = new CancellationTokenSource();

        await limiter.WaitAsync(CancellationToken.None);
        var waitingRequest = limiter.WaitAsync(cancellationSource.Token);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingRequest);
    }

    [Fact]
    public async Task WaitAsync_does_not_release_a_permit_when_the_wall_clock_jumps_forward()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, timeProvider);

        await limiter.WaitAsync(CancellationToken.None);

        timeProvider.AdvanceUtcNow(TimeSpan.FromMinutes(10));
        var nextRequest = limiter.WaitAsync(CancellationToken.None);

        Assert.False(nextRequest.IsCompleted);
    }

    [Fact]
    public async Task WaitAsync_does_not_extend_the_permit_window_when_the_wall_clock_jumps_backward()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, timeProvider);

        await limiter.WaitAsync(CancellationToken.None);

        timeProvider.AdvanceUtcNow(TimeSpan.FromMinutes(-10));
        var nextRequest = limiter.WaitAsync(CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await nextRequest;
    }
}

internal sealed class RateLimiterTestTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<RateLimiterTestTimer> _timers = [];
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _timestamp;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new RateLimiterTestTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<RateLimiterTestTimer> dueTimers;

        lock (_sync)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
            dueTimers = _timers.Where(timer => timer.IsDue(_timestamp)).ToList();
        }

        foreach (var timer in dueTimers)
        {
            timer.Fire();
        }
    }

    public void AdvanceUtcNow(TimeSpan duration)
    {
        lock (_sync)
        {
            _utcNow += duration;
        }
    }

    private void AddTimer(RateLimiterTestTimer timer)
    {
        lock (_sync)
        {
            _timers.Add(timer);
        }
    }

    private sealed class RateLimiterTestTimer : ITimer
    {
        private readonly RateLimiterTestTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long? _dueTimestamp;
        private bool _disposed;

        public RateLimiterTestTimer(RateLimiterTestTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _owner.AddTimer(this);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.GetTimestamp() + dueTime.Ticks;
            return true;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool IsDue(long timestamp) => !_disposed && _dueTimestamp is { } dueTimestamp && dueTimestamp <= timestamp;

        public void Fire()
        {
            if (_disposed)
            {
                return;
            }

            _dueTimestamp = null;
            _callback(_state);
        }
    }
}
