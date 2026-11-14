namespace GenWave.Memory;

/// <summary>The show currently on the air, as the accrual consumer has watched it so far (SPEC F218.15).</summary>
sealed class ShowRun(long showId, long? personaId, string showName, DateTimeOffset startedAt, bool partial)
{
    /// <summary>
    /// True when the consumer first saw this show already under way (a restart mid-show): its count and first
    /// track are incomplete, so no episode is written for it rather than a "hosted" memory that is half true.
    /// </summary>
    public bool Partial { get; } = partial;

    public long ShowId { get; } = showId;

    public long? PersonaId { get; } = personaId;

    public string ShowName { get; } = showName;

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
