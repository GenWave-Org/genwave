using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Authored-insert catalog seam (F27.1, F27.2, F27.8): lands a generated safe-segment artifact as a
/// normal <c>library.media</c> row, directly in <c>state='ready'</c>, with no enricher round-trip.
/// Kept separate from <see cref="IMediaCatalog"/> (scoped reads) and <see cref="IAdminMediaWrite"/>
/// (operator patch of an existing row) because its caller is a generation pipeline creating a brand
/// new row, not a request handler mutating one that already exists.
/// </summary>
public interface IAuthoredCatalogWriter
{
    /// <summary>
    /// Inserts <paramref name="insert"/> as a single INSERT ... RETURNING id — one round-trip, so a
    /// rejected insert (see below) writes nothing.
    /// </summary>
    /// <returns>The id of the newly inserted row.</returns>
    /// <remarks>
    /// When <see cref="AuthoredMediaInsert.LibraryId"/> references no row in <c>library.library</c>,
    /// the underlying foreign-key violation (Postgres SQLSTATE 23503) propagates to the caller
    /// unmapped — the insert never committed, so nothing is written (F27.1 sad path).
    /// </remarks>
    Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct);

    /// <summary>
    /// Sets an already-inserted row's <c>eligible</c> column directly (SPEC F161.3, F159.3; STORY-391;
    /// PLAN T401, widened at review F7). <c>eligible: true</c> is the SECOND half of the ad-spot
    /// two-round-trip authored tail: <see cref="InsertAuthoredAsync"/> lands <paramref name="mediaId"/>'s
    /// row with <see cref="AuthoredMediaInsert.Eligible"/> = <see langword="false"/>, and only a
    /// caller who has since confirmed its OWN bookkeeping is consistent (for ads:
    /// <c>IAdSpotStore.MarkReadyAsync</c> returned <see langword="true"/> — station-role, a separate
    /// schema this seam never crosses, the db/22 boundary) calls this method with
    /// <c>eligible: true</c> to make the row actually airable. A crash, or a declined confirmation,
    /// between the insert and this call leaves the row ineligible — inert, never an AIRABLE orphan the
    /// F158.5 picker could still find (the gh-#610 family this ordering exists to close), and not a
    /// novel debris class either: SPEC F159.3 already ships this EXACT <c>eligible=false</c>,
    /// never-deleted shape for a routinely-retired ready spot's own media row.
    ///
    /// <para>
    /// <c>eligible: false</c> is the SAME column write in the OTHER direction — SPEC F159.3's own
    /// refresh path (a <c>ready</c> spot older than <c>RefreshDays</c> retires and its media row is
    /// set <c>eligible=false</c>) — widened onto this ONE seam at review F7 rather than adding a
    /// second, narrower method, so a LATER caller (PLAN T402's own stock pass) never needs a THIRD
    /// eligibility-writing method on this interface just to flip the other direction.
    /// </para>
    /// </summary>
    /// <param name="mediaId">The id <see cref="InsertAuthoredAsync"/> returned.</param>
    /// <param name="eligible">The value to set.</param>
    /// <returns>
    /// <see langword="true"/> when a row with this id was found and set; <see langword="false"/>
    /// when no such row exists (never thrown — the same "Total" posture
    /// <c>IAdSpotStore.MarkReadyAsync</c>/<c>MarkFailedAsync</c> already establish one seam over).
    /// </returns>
    Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct);
}
