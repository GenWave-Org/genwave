namespace GenWave.Core.Domain;

/// <summary>
/// The narrow playout-facing view of a track (PRD §4.1, SEAM 1): exactly what the feeder needs to
/// push a track and compute gain, and nothing more. A <see cref="MediaReference"/> NARROWS to this —
/// the feeder seam stays minimal while the catalog exposes everything else.
/// </summary>
/// <param name="MediaId">Opaque library id — the caller never parses or interprets it.</param>
/// <param name="Locator">Resolves to the engine's /media path today; an object key tomorrow.</param>
/// <param name="Title">Display title (now-playing).</param>
/// <param name="Loudness">Measured by the library; gain is APPLIED by playout at push time.</param>
/// <param name="Artist">Display artist (now-playing, TTS patter); null until enriched or for non-music segments.</param>
/// <param name="Cue">Silence-trimmed cue points measured at scan time; null until enriched.</param>
/// <param name="IntroEnergy">Normalized [0, 1] intro energy measured at scan time; null until enriched.</param>
/// <param name="OutroEnergy">Normalized [0, 1] outro energy measured at scan time; null until enriched.</param>
/// <param name="Album">Display album (DJ blurb tags, F34.7); null until enriched or for non-music segments.</param>
/// <param name="Genre">Display genre (DJ blurb tags, F34.7); null until enriched or for non-music segments.</param>
/// <param name="Year">Release year (DJ blurb tags, F34.7); null until enriched or for non-music segments.</param>
/// <param name="DurationMs">
/// Track duration in milliseconds (on-air duration, SPEC F50.1); null for engine-initiated plays.
/// <c>tts:*</c> patter segments carry the cue analyzer's measured CueOutSec, rounded to the nearest
/// millisecond, when cue analysis succeeded (SPEC F66.1) — null when it failed. Never fabricated.
/// </param>
public sealed record MediaItem(string MediaId, string Locator, string Title, Loudness Loudness, string? Artist = null, CuePoints? Cue = null, double? IntroEnergy = null, double? OutroEnergy = null, string? Album = null, string? Genre = null, int? Year = null, int? DurationMs = null);
