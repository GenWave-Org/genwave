namespace GenWave.Core.Domain;

/// <summary>
/// Narrows the full catalog projection (<see cref="MediaReference"/>) down to the playout-facing
/// <see cref="MediaItem"/> (PRD §4.1, SEAM 1). This is the one production mapping every
/// music-selection call site shares (the Orchestrator, the v1 random-selection provider, the safe-track
/// endpoint) — a field added to <see cref="MediaItem"/> only ever has to widen here, never separately
/// at each call site.
/// </summary>
public static class MediaReferenceExtensions
{
    public static MediaItem ToMediaItem(this MediaReference reference) =>
        new(reference.MediaId, reference.Locator, reference.Title, reference.Loudness, reference.Artist,
            reference.Cue, reference.IntroEnergy, reference.OutroEnergy, reference.Album, reference.Genre,
            reference.Year, reference.DurationMs);
}
