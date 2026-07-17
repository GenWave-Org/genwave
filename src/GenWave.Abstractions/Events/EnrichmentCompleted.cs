namespace GenWave.Core.Events;

/// <summary>
/// First-pass enrichment finished for a media row: the row flipped to <c>ready</c>
/// (<paramref name="Succeeded"/> = true) or <c>failed</c> (false).
/// </summary>
public sealed record EnrichmentCompleted(long MediaId, bool Succeeded) : StationEvent;
