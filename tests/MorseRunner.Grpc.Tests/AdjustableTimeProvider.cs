namespace MorseRunner.Grpc.Tests;

internal sealed class AdjustableTimeProvider(DateTimeOffset current) :
    TimeProvider
{
    private readonly List<AdjustableTimer> _timers = [];
    private readonly object _gate = new();

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return current;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new AdjustableTimer(
            this,
            callback,
            state,
            current + dueTime,
            period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        AdjustableTimer[] due;
        lock (_gate)
        {
            current += duration;
            due = _timers
                .Where(timer => timer.IsDue(current))
                .ToArray();
            foreach (AdjustableTimer timer in due)
            {
                timer.ScheduleNext(current);
            }
        }

        foreach (AdjustableTimer timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(AdjustableTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class AdjustableTimer(
        AdjustableTimeProvider owner,
        TimerCallback callback,
        object? state,
        DateTimeOffset dueAt,
        TimeSpan period) : ITimer
    {
        private bool _disposed;

        public bool IsDue(DateTimeOffset now) =>
            !_disposed && dueAt <= now;

        public void ScheduleNext(DateTimeOffset now)
        {
            dueAt = period == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : now + period;
        }

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
        {
            if (_disposed)
            {
                return false;
            }

            dueAt = owner.GetUtcNow() + dueTime;
            period = newPeriod;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
