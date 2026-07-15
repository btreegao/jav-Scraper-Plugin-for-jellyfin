namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class SlidingWindowRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _sync = new();
    private readonly Queue<long> _requests = new();
    private readonly int _maxRequests;
    private readonly TimeProvider _timeProvider;

    public SlidingWindowRateLimiter(int maxRequests, TimeProvider? timeProvider = null)
    {
        if (maxRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequests), maxRequests, "The request limit must be positive.");
        }

        _maxRequests = maxRequests;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan waitDuration;
            lock (_sync)
            {
                var now = _timeProvider.GetTimestamp();
                RemoveExpiredRequests(now);

                if (_requests.Count < _maxRequests)
                {
                    _requests.Enqueue(now);
                    return;
                }

                waitDuration = Window - _timeProvider.GetElapsedTime(_requests.Peek(), now);
            }

            await Task.Delay(waitDuration, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RemoveExpiredRequests(long now)
    {
        while (_requests.TryPeek(out var requestTime)
            && _timeProvider.GetElapsedTime(requestTime, now) >= Window)
        {
            _requests.Dequeue();
        }
    }
}
