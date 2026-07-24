using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F87.5, STORY-226, PLAN T89 — the one question the listener-request matcher needs answered
/// across the station/library schema boundary: does the catalog already hold something for this
/// parsed artist/title? Resolved on the LIBRARY connection (mirrors <see cref="IMediaLibraryMembership"/>'s
/// own rationale) — <c>station_svc</c> has no grant on <c>library.media</c>, so the Host composes the
/// station-side matcher over this narrow seam rather than a cross-schema SQL join.
///
/// <para>
/// Deliberately its own interface rather than a new <see cref="IMediaCatalog"/> member: this seam's
/// one concern (best-match lookup for a request) never grows the read-amplification a general
/// catalog query surface invites, and every existing <see cref="IMediaCatalog"/> fake keeps compiling.
/// </para>
///
/// <para>
/// A hit here is not the last word: <c>never_play</c> can be flipped by an operator AFTER a request
/// is matched. This probe only ever answers "at match time" — SPEC F87.6's fulfillment rung (PLAN T90,
/// <see cref="GetSelectableByIdAsync"/>/<see cref="FindVibeAsync"/> below) re-checks the same veto
/// predicate immediately before a matched request actually airs.
/// </para>
/// </summary>
public interface IRequestCatalogProbe
{
    /// <summary>
    /// Returns the best-matching media id for <paramref name="artist"/>/<paramref name="title"/>
    /// among rows that are <c>ready</c>, <c>eligible</c>, and not flagged <c>never_play</c> — operator
    /// vetoes are law (SPEC F87.5). <see langword="null"/> when neither predicate is given, or no row
    /// clears the WHERE clause at all.
    /// </summary>
    Task<long?> FindBestAsync(string? artist, string? title, CancellationToken ct);

    /// <summary>
    /// SPEC F87.6, STORY-227, PLAN T90 — the fulfillment rung's law re-check for a T89-matched
    /// request, immediately before it airs: re-applies the exact <see cref="FindBestAsync"/>
    /// selectability predicate (<c>ready</c>, <c>measurable</c>, <c>eligible</c>, not
    /// <c>never_play</c> — operator vetoes are law, and measurable joins them here for T89 parity: a
    /// matched-but-unmeasurable row must not idle a request to expiry for nothing) PLUS the gh-#99
    /// safe-scope exclusion, unconditionally regardless of <paramref name="envelope"/>.
    /// <paramref name="envelope"/> is the ADDITIONAL, mode-dependent leg (SPEC F87.6's
    /// <c>OverrideEnvelope</c> switch): <see langword="null"/> means bypass genre/energy entirely
    /// (<c>OverrideEnvelope=true</c>, the default); a supplied envelope ANDs its genre allow-list and
    /// energy band into the same WHERE clause, by construction (SPEC F81.4's own precedent), so a row
    /// outside it never reaches this method's result at all. <see langword="null"/> when the id
    /// doesn't exist, fails a law predicate, is safe-scope content, or (only when
    /// <paramref name="envelope"/> is supplied) falls outside it — every one of those is "not
    /// selectable right now," collapsed into one null exactly like <see cref="FindBestAsync"/>'s own
    /// contract, since the fulfillment rung reacts identically to all of them: this attempt fails, the
    /// request stays pending for the next one.
    /// </summary>
    Task<MediaReference?> GetSelectableByIdAsync(long mediaId, SegmentEnvelope? envelope, CancellationToken ct);

    /// <summary>
    /// SPEC F87.6, STORY-227, PLAN T90 — resolves a vibe request (a T89 miss that kept a non-empty
    /// mood predicate, F87.5) through the existing mood-filter machinery (F86.8 semantics: array
    /// overlap against <c>library.media.moods</c>) rather than a specific known id. Applies the SAME
    /// law predicate and safe-scope exclusion <see cref="GetSelectableByIdAsync"/> does (gh-#99's
    /// lesson applies here too — a vibe match was never vetted against never-play/safe-scope at parse
    /// time the way a T89 catalog match was), plus the SAME <paramref name="envelope"/> switch
    /// (<see langword="null"/> = bypass genre/energy). Orders by rating score (descending, nulls
    /// last) then randomly among ties — the same "score only breaks ties, never governs odds beyond
    /// that" posture <see cref="FindBestAsync"/>'s own exact-match tie-break establishes. Returns at
    /// most one row; <see langword="null"/> when <paramref name="moods"/> is empty or nothing in scope
    /// clears every predicate.
    /// </summary>
    Task<MediaReference?> FindVibeAsync(IReadOnlyList<string> moods, SegmentEnvelope? envelope, CancellationToken ct);
}
