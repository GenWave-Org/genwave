using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F115.4 — the narrow cross-schema answer the show delete guard needs: which
/// <c>library.media</c> rows are scoped (<c>show_id</c>, F117.1 — no FK) to a show that has just been
/// deleted? <c>station_svc</c> deliberately has no grant on <c>library.media</c> (the db/22 boundary —
/// the same reason <see cref="IMediaLibraryMembership"/> resolves ITS cross-schema question on the
/// library connection instead of a join), so this seam does too.
///
/// Deliberately its own narrow interface, mirroring <see cref="IMediaLibraryMembership"/>'s own "one
/// question, its own seam" posture: <c>ShowsController</c>'s delete guard is the only consumer, and
/// the question — "which rows, and clear them" — never grows read-amplification temptations onto a
/// wider media interface.
/// </summary>
public interface IShowImagingScope
{
    /// <summary>
    /// Best-effort orphan prevention (SPEC F115.4): clears <c>show_id</c> on every <c>library.media</c>
    /// row currently scoped to <paramref name="showId"/> and returns exactly the rows cleared, in
    /// <c>id</c> order — the delete guard's response names them in the SAME round trip that unscopes
    /// them (an <c>UPDATE ... RETURNING</c>, atomic — no separate SELECT-then-UPDATE). Idempotent: a
    /// repeat call against an already-cleared <paramref name="showId"/> matches nothing and returns an
    /// empty list, never an error — <c>library.media.show_id</c> carries no FK to violate, so there is
    /// nothing for a second write to conflict with.
    /// </summary>
    Task<IReadOnlyList<ScopedImagingRow>> UnscopeAsync(long showId, CancellationToken ct);
}
