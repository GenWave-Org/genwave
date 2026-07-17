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
    double? TrackEnergy = null);
