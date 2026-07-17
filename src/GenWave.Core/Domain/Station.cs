namespace GenWave.Core.Domain;

/// <summary>
/// A broadcast station row from <c>directory.station</c> (ARCHITECTURE.md §Phase 2).
/// The <c>loudness</c> JSONB column is omitted — not needed by the feeder playout path yet (YAGNI).
/// </summary>
public sealed record Station(
    long Id,
    string Name,
    string ListenerFqdn,
    string EngineHost,
    string IcecastHost,
    CadenceConfig Cadence,
    DateTimeOffset CreatedAt
);
