namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> whose timers fire only when the test says so, and which remembers how long each was set for.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];

    /// <summary>The due time of every timer created so far, in creation order.</summary>
    internal IReadOnlyList<TimeSpan> DueTimes => timers.Select(t => t.DueTime).ToList();

    internal void FireAll()
    {
        foreach (ManualTimer timer in timers.ToList())
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(callback, state, dueTime);
        timers.Add(timer);
        return timer;
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        internal TimeSpan DueTime { get; private set; } = dueTime;

        internal void Fire() => callback(state);

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            DueTime = newDueTime;
            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
