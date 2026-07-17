namespace GenWave.Core.Domain;

/// <summary>
/// Simple cadence toggles that control how often voice segments are woven into the broadcast.
/// Not a strategy engine — just the knobs an operator can reach for v1.
/// </summary>
public sealed record CadenceConfig
{
    /// <summary>When true a lead-in segment precedes every track.</summary>
    public bool LeadInBeforeEachTrack { get; init; } = true;

    /// <summary>When true a back-announce segment follows every track.</summary>
    public bool BackAnnounceAfterEachTrack { get; init; } = true;

    /// <summary>A station-id segment is inserted every N tracks. Zero disables station-id segments.</summary>
    public int StationIdEveryNUnits { get; init; } = 4;
}
