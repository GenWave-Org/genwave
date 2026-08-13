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
/// <param name="PersonaPick">
/// SPEC F82.6, F83.1 (STORY-213, PLAN T64) — the persona ranker's diagnostics for this pick, carried
/// straight from <see cref="RotationCandidate.PersonaPick"/> once <c>Orchestrator.GetNextAsync</c>
/// narrows the winning candidate to this item; <see langword="null"/> for every envelope-only ladder
/// pick, including the common persona-off case. This item rides on into <see cref="SegmentRequest.Track"/>
/// as the previous/next track for a back-announce/lead-in, so a future copywriter consumer (T65) can
/// read <see cref="PersonaPickDiagnostics.FiredRules"/>/<see cref="PersonaPickDiagnostics.IsExploration"/>
/// off whichever track is airing, with no separate lookup.
/// </param>
/// <param name="RequestFulfilled">
/// SPEC F87.6/F87.7 (STORY-227, PLAN T90) — <see langword="true"/> when this track was pulled onto
/// air by the fulfillment rung's short-circuit rather than persona/envelope selection, carried
/// straight from <see cref="RotationCandidate.RequestFulfilled"/> the same way <see cref="PersonaPick"/>
/// rides across from <see cref="RotationCandidate.PersonaPick"/>. Reaches <see cref="SegmentRequest.Track"/>
/// for the lead-in <c>Orchestrator.EnqueuePatterAsync</c> builds for this exact track, so a future
/// copywriter consumer (T91) can color the lead-in with a generic "got this one in from the request
/// line" acknowledgment — never the wish text or parsed predicates (F87.7's disclosure law).
/// </param>
/// <param name="DjName">
/// gh-#259 — the display name of the DJ persona this item airs UNDER, stamped at plan/render time
/// and carried with the item through the engine queue, so Now Playing attribution follows the item
/// actually on air rather than the schedule's live answer (which flips at the boundary while
/// already-queued items are still draining). For a TTS segment this is the persona whose voice
/// reads it (<c>SegmentRequest.PersonaName</c> — the outgoing/incoming persona for a handoff
/// piece); for a music track, the persona whose show it was planned in; for a station ID, the
/// unit's show persona (the credit stays the station's — <see cref="Artist"/> is a separate field,
/// gh-#96 untouched). <see langword="null"/> = no DJ (gap/music-only segment, or a pre-gh-#259
/// caller that never stamps it).
/// </param>
/// <param name="SegmentKind">
/// SPEC F113.1 (STORY-304, PLAN T220) — the demo-hour observability instrument's own token:
/// <see langword="null"/> for every music/audio row (the only caller is
/// <c>MediaReferenceExtensions.ToMediaItem</c>, which never sets this), the segment's own
/// <see cref="SegmentRequest.Kind"/> for a TTS render (<c>TtsSegmentSource.RenderAsync</c> stamps
/// it verbatim). Rides through <c>PlayoutFeeder</c>'s push-time metadata into
/// <see cref="GenWave.Core.Events.TrackAired.SegmentKind"/> the same way <see cref="DjName"/> does,
/// so the booth log's <c>track-started</c> row can stamp a structured <c>segment_kind</c> at the
/// genuine AIR-time instant — never at render time, when a budget-dropped piece never actually airs.
/// </param>
public sealed record MediaItem(string MediaId, string Locator, string Title, Loudness Loudness, string? Artist = null, CuePoints? Cue = null, double? IntroEnergy = null, double? OutroEnergy = null, string? Album = null, string? Genre = null, int? Year = null, int? DurationMs = null, PersonaPickDiagnostics? PersonaPick = null, bool RequestFulfilled = false, string? DjName = null, SegmentKind? SegmentKind = null);
