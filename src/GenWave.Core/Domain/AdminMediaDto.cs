namespace GenWave.Core.Domain;

/// <summary>
/// Admin-only projection of a media catalog row returned by admin endpoints (T048).
/// Richer than the playout <see cref="MediaReference"/>: includes <c>state</c>, <c>format</c>,
/// and all enrichment columns in a single flat shape so the admin UI receives one JSON object
/// with no nested loudness sub-object.
///
/// <c>Version</c> is the Postgres system column <c>xmin</c> serialized as a string. The
/// <c>GET /api/media/{id}</c> endpoint returns it as a weak ETag (<c>W/"&lt;xmin&gt;"</c>) for
/// optimistic-concurrency control on <c>PATCH /api/media/{id}</c> (W2).
///
/// <c>Score</c> and <c>NeverPlay</c> carry the row's rating state (SPEC F33.10), resolved via a
/// LEFT JOIN + COALESCE against <c>library.media_rating</c> — an unrated row reads the F33.2
/// ledger default (score 50, not flagged). Rating writes never touch <c>library.media</c>'s
/// <c>xmin</c> (F33.1), so <c>Version</c>/the ETag are unaffected by a vote or never-play toggle;
/// the default values here exist only so older call sites that construct this record without
/// naming these two fields keep compiling.
///
/// <c>Bpm</c> and <c>TrackEnergy</c> (SPEC F49.2) are the Enrichment 2.0 signals: <c>Bpm</c> is
/// the tempo estimate (F46.1, null until analyzed); <c>TrackEnergy</c> is the whole-track
/// perceptual energy generated column (F47.1, null until <c>integrated_lufs</c> is measured).
/// Both ride the same single browse/detail projection as every other enrichment column — the
/// default values here exist only so older call sites keep compiling without naming them.
///
/// <c>Moods</c> (SPEC F86.8) surfaces the fixed-vocabulary mood tags (F85.1) a track has been
/// assigned; <c>null</c> for a row the mood tagger hasn't reached (or missed) yet — never an
/// empty array standing in for "untagged", so the UI can tell "no moods yet" apart from "tagged,
/// zero survivors" if that distinction ever matters.
///
/// <c>Explicit</c>/<c>ExplicitSource</c> (SPEC F95.2, STORY-251) surface the per-track
/// explicit/advisory classification: <c>Explicit</c> is <see langword="null"/> until classified
/// (never a sentinel false), and <c>ExplicitSource</c> names who classified it —
/// <c>tag</c>/<c>llm</c>/<c>operator</c> — once it has been. db/26 ships the columns and this
/// projection only; no classification pipeline writes them yet (T110), and nothing enforces or
/// filters on them here — <c>Explicit</c> is orthogonal to the F95.5 never-play verdict.
///
/// <c>ImagingKind</c> (gh-#149, SPEC F158.1) surfaces the Station Imaging content kind of an
/// authored segment as its storage token — <c>liner</c>/<c>station_id</c>/<c>jingle</c>/
/// <c>promo</c>/<c>ad</c> — and is <see langword="null"/> for scanned rows and for authored rows
/// that predate db/30. Display-only from THIS projection's own perspective; it is no longer true
/// that playout ignores the column entirely — <c>StationId</c> already drove its own selector
/// (F110.2), and SPEC F158.4 (PLAN T395) now fences every non-null kind, <c>ad</c> included, out of
/// music rotation/envelopes/requests/<c>/media/random</c> regardless of scope.
///
/// <c>ShowId</c> (SPEC F117.1, STORY-313, PLAN T246) surfaces <c>library.media.show_id</c> — the
/// show an authored imaging row is scoped to, or <see langword="null"/> for station-wide (the only
/// meaning every pre-F117 row carries). The Admin UI resolves this id against its own already-loaded
/// show roster (<c>GET /api/shows</c>) to display a name; this DTO never joins across the db/22
/// schema/role boundary to fetch one itself.
///
/// <c>Plays</c>/<c>FirstAiredAt</c>/<c>LastAiredAt</c> (SPEC F149.5, STORY-368, PLAN T371) surface
/// <c>library.media_rotation</c>'s own <c>play_count</c>/<c>first_aired_at</c>/<c>last_aired_at</c> —
/// all three <see langword="null"/> together for a row that has never aired (no ledger row at all,
/// never a sentinel 0/false). Only the single-row detail read (<c>GET /api/media/{id}</c>) populates
/// them; the paged browse projection leaves them null, the same "only populated by projections that
/// select it" posture <c>Bpm</c>/<c>TrackEnergy</c> already establish above.
/// </summary>
public sealed record AdminMediaDto(
    string MediaId,
    string Locator,
    string Format,
    string State,
    int? DurationMs,
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    double? IntegratedLufs,
    double? TruePeakDbtp,
    bool? Measurable,
    double? CueInSec,
    double? CueOutSec,
    bool Eligible,
    string Version,
    int Score = 50,
    bool NeverPlay = false,
    double? Bpm = null,
    double? TrackEnergy = null,
    IReadOnlyList<string>? Moods = null,
    bool Rateable = true,
    bool? Explicit = null,
    string? ExplicitSource = null,
    string? ImagingKind = null,
    long? ShowId = null,
    int? Plays = null,
    DateTimeOffset? FirstAiredAt = null,
    DateTimeOffset? LastAiredAt = null);
