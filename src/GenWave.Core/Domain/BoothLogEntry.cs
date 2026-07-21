namespace GenWave.Core.Domain;

/// <summary>
/// One narrative row read back from <c>station.booth_log</c> (SPEC F72.1-F72.3, STORY-195) — the
/// operator-readable "what the DJ did and said" feed: track starts, patter airs, degradation mode
/// changes. <see cref="Summary"/> is always human language, never a JSON dump.
/// </summary>
public sealed record BoothLogEntry(long Id, DateTime OccurredAt, string Kind, string Summary);
