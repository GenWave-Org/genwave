namespace GenWave.Memory;

/// <summary>The show (or, with no show attached, the DJ's shift) currently on the air, as the accrual consumer has watched it so far (SPEC F218.15).</summary>
sealed class OnAirRun(RunKey key, long? personaId, string? showName, DateTimeOffset startedAt, bool partial)
{
    /// <summary>
    /// True when the consumer first saw this run already under way (a restart mid-show or mid-shift): its count and first
    /// track are incomplete, so no episode is written for it rather than a "hosted" memory that is half true.
    /// </summary>
    public bool Partial { get; } = partial;

    /// <summary>What makes this run one run: see <see cref="RunKey"/>.</summary>
    public RunKey Key { get; } = key;

    public long? PersonaId { get; } = personaId;

    /// <summary>The named show's name; null for a shift (a DJ-only slot with no show attached).</summary>
    public string? ShowName { get; } = showName;

    /// <summary>The show's scheduled start, station-local.</summary>
    public DateTimeOffset StartedAt { get; } = startedAt;

    public int TrackCount { get; private set; }

    public string? FirstTrack { get; private set; }

    public string? LastTrack { get; private set; }

    public void CountTrack(string? track)
    {
        TrackCount++;
        FirstTrack ??= track;
        LastTrack = track ?? LastTrack;
    }
}
