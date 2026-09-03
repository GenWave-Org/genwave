namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>station.ad_spot</c> (SPEC F159.1, F159.2; STORY-389; PLAN T398) —
/// <c>Abstractions.IAdSpotStore</c>'s own element type. <see cref="Version"/> is the Postgres system
/// column <c>xmin</c> serialized as a string (mirrors <see cref="AdminMediaDto"/>'s own
/// <c>Version</c>/weak-ETag idiom) — every xmin-guarded transition on <c>Abstractions.IAdSpotStore</c>
/// takes the PREVIOUS read's own <see cref="Version"/> back as <c>expectedVersion</c>.
/// </summary>
/// <param name="Id">The row's own surrogate key — stable across every transition (nothing is ever
/// deleted, SPEC F159.1).</param>
/// <param name="Brand">The fictional (or owner's real) brand this spot advertises.</param>
/// <param name="Title">A short operator-facing label — never read aloud.</param>
/// <param name="Brief">The premise/tone/structure hint this spot's script was written from, or
/// <see langword="null"/> for an owner-authored spot with no separate brief.</param>
/// <param name="Script">The spot's own line-by-line copy, or <see langword="null"/> before generation
/// completes.</param>
/// <param name="Source">Where this spot's copy came from (SPEC F159.1).</param>
/// <param name="PackSlug">Set only for a <see cref="AdSource.Pack"/> spot (SPEC F159.1) —
/// <see langword="null"/> for every other source.</param>
/// <param name="SpotSeconds">One of the three shipped structures — 15, 30, or 60 (SPEC F160.2).</param>
/// <param name="VoicePlan">The rendered <c>VoiceSpec</c> cast, as raw <c>jsonb</c> text (SPEC F161.2)
/// — deliberately opaque here, the same <c>RotFinding.Evidence</c>/<c>FontPack.Definition</c>
/// precedent: a caller downstream of this Core seam reconstitutes the shape it expects at its own
/// edge.</param>
/// <param name="BedMediaId">An optional background bed track's <c>library.media</c> id — plain, no
/// FK (the db/22 schema-role boundary; ids resolve through <c>IMediaCatalog</c>, never a cross-schema
/// join).</param>
/// <param name="State">The spot's current lifecycle state (SPEC F159.2).</param>
/// <param name="FailReason">Non-null exactly when <see cref="State"/> is <see cref="AdState.Failed"/>
/// (SPEC F159.2, enforced by the store and by db/43's own <c>CHECK</c>).</param>
/// <param name="MediaId">The rendered <c>library.media</c> id, plain, no FK (same db/22 boundary as
/// <see cref="BedMediaId"/>) — non-null exactly when <see cref="State"/> is
/// <see cref="AdState.Ready"/> (SPEC F159.2, enforced by the store and by db/43's own
/// <c>CHECK</c>).</param>
/// <param name="Generation">Column parity with db/42's own schema (SPEC F159.1) — reserved, not
/// yet bumped by any transition on this store: nothing here re-renders a row in place, and SPEC
/// F159.3's own refresh path is retire-then-refill (a brand-new row on a fresh generation of the
/// campaign, not a re-render of this same row), so no T398 caller has a "same row, new attempt"
/// case to stamp this for. Left for a future consumer that does.</param>
/// <param name="CreatedAt">When this spot was first created.</param>
/// <param name="StateChangedAt">When <see cref="State"/> last changed — every legal transition
/// stamps this (SPEC F159.2).</param>
/// <param name="RenderedAt">When this spot last entered <see cref="AdState.Ready"/> —
/// <see langword="null"/> until then.</param>
/// <param name="RetiredAt">When this spot entered <see cref="AdState.Retired"/> —
/// <see langword="null"/> until then.</param>
/// <param name="Version">The row's <c>xmin</c>, as a string — the optimistic-concurrency token every
/// operator-facing transition (<c>ApproveAsync</c>/<c>RetryAsync</c>/<c>RetireAsync</c>) takes back
/// as <c>expectedVersion</c>.</param>
public sealed record AdSpot(
    long Id,
    string Brand,
    string Title,
    string? Brief,
    string? Script,
    AdSource Source,
    string? PackSlug,
    int SpotSeconds,
    string? VoicePlan,
    long? BedMediaId,
    AdState State,
    string? FailReason,
    long? MediaId,
    int Generation,
    DateTime CreatedAt,
    DateTime StateChangedAt,
    DateTime? RenderedAt,
    DateTime? RetiredAt,
    string Version);
