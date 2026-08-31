namespace GenWave.Core.Domain;

/// <summary>
/// One <see cref="RotFinding"/> joined to the <c>library.media</c> row it is about (SPEC F153.9;
/// STORY-374; PLAN T377) — <see cref="Abstractions.IRotFindingStore.ListWithMediaAsync"/>'s own
/// element type. The admin surface's grouped listing (dead-file/near-duplicate/stale-metadata/
/// shelf-dust/unreachable, and the near-duplicate group's own member rows) needs a track's path,
/// duration, plays, and rating alongside the finding itself so the operator can act on a row without
/// a second per-id lookup (T377's own "ONE new joined read, not N lookups" ruling) — this record is
/// that one flattened shape, never reached by any reconcile pass.
/// </summary>
/// <param name="Finding">The finding row itself — <see cref="RotFinding.MediaId"/> IS the media id
/// this row is about (T377 review LOW-1: no second top-level <c>MediaId</c> — a caller reaches
/// <c>Finding.MediaId</c> directly rather than this record carrying the same value twice).</param>
/// <param name="Locator">The engine-visible path (<c>library.media.path</c>).</param>
/// <param name="Title">The track's title, or <see langword="null"/> if never tagged.</param>
/// <param name="Artist">The track's artist, or <see langword="null"/> if never tagged.</param>
/// <param name="DurationMs">The track's duration, or <see langword="null"/> before enrichment.</param>
/// <param name="Plays"><c>library.media_rotation.play_count</c>, <c>0</c> when no ledger row exists
/// for this media id — never <see langword="null"/>, the same "0, not absent" posture
/// <c>RotationHealth.NeverAired</c> already treats a missing ledger row as.</param>
/// <param name="Rating"><c>library.media_rating.score</c>, <see langword="null"/> when no rating row
/// exists — deliberately NOT defaulted to the F33.2 ledger default of 50 the way
/// <c>AdminMediaDto.Score</c> is, since "never rated" and "rated exactly 50" are genuinely different
/// facts an operator triaging a finding needs to tell apart.</param>
/// <param name="NeverPlay"><c>library.media_rating.never_play</c>, <see langword="false"/> when no
/// rating row exists (the same default the row itself carries).</param>
/// <param name="Eligible"><c>library.media.eligible</c> — whether an operator has excluded the track
/// from playout.</param>
public sealed record RotFindingWithMedia(
    RotFinding Finding,
    string Locator,
    string? Title,
    string? Artist,
    int? DurationMs,
    int Plays,
    int? Rating,
    bool NeverPlay,
    bool Eligible);
