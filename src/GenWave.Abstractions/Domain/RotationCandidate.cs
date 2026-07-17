namespace GenWave.Core.Domain;

/// <summary>
/// The result of <see cref="Abstractions.IMediaCatalog.GetRotationCandidateAsync"/> (SPEC F41.1) — a
/// selected track plus which preference tiers were relaxed to produce it. Either flag being
/// <c>true</c> is diagnostic (F41.5: the Orchestrator logs a WARN naming the relaxed constraint), not
/// an error — the never-drains contract (F41.2/F41.4) means a relaxed pick still beats null.
/// </summary>
public sealed record RotationCandidate(
    MediaReference Media,
    bool RepeatedRecent,
    bool RepeatedArtist);
