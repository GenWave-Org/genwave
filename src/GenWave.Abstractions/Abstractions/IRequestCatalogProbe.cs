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
/// is matched. This probe only ever answers "at match time" — SPEC F87.6's fulfillment rung (PLAN T90)
/// re-checks the same veto predicate immediately before a matched request actually airs.
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
}
