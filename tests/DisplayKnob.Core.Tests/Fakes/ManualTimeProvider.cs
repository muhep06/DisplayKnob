namespace DisplayKnob.Core.Tests.Fakes;

/// <summary>
/// A clock that only moves when a test says so, and only to the next moment something is waiting for.
/// </summary>
/// <remarks>
/// Unlike a clock that is advanced by a fixed step, this one can't run ahead of the code under test: time stops at
/// the next timer (a <c>Task.Delay</c> on this provider) and goes no further, so the gaps between the things the code
/// does are exactly the delays it asked for.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock to the earliest pending timer and fires it. <see langword="false"/> if none is pending.</summary>
    public bool TryAdvanceToNextTimer()
    {
        ManualTimer? next;
        lock (_lock)
        {
            next = _timers.Where(t => t.Due is not null).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null)
            {
                return false;
            }

            if (next.Due > _now)
            {
                _now = next.Due.Value;
            }
        }

        next.Fire();
        return true;
    }

    /// <summary>
    /// Lets time pass, timer by timer, until <paramref name="condition"/> holds. When nothing is waiting yet the code
    /// under test is still running, so this yields instead of moving the clock.
    /// </summary>
    public async Task RunUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "Timed out waiting for the code under test.");
            if (!TryAdvanceToNextTimer())
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>Lets everything that is already due or waiting run to its end (no new work is expected).</summary>
    public async Task SettleAsync()
    {
        // A fixed number of rounds: this is for "nothing else happens", which can't be waited for.
        for (var i = 0; i < 2_000; i++)
        {
            if (!TryAdvanceToNextTimer())
            {
                await Task.Yield();
            }
        }
    }

    private void Add(ManualTimer timer)
    {
        lock (_lock)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
            }
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // Only one-shot timers are used here (Task.Delay); a period is ignored.
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.GetUtcNow() + dueTime;
            if (Due is not null)
            {
                _owner.Add(this);
            }

            return true;
        }

        public void Fire()
        {
            Due = null;
            _callback(_state);
        }

        public void Dispose() => _owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
