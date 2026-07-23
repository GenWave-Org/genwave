using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Admin catalog write operations (STORY-039, Epic I).
/// Kept separate from <see cref="IAdminMediaQuery"/> and <see cref="IAdminMediaLookup"/> so the
/// read path is not coupled to write concerns and test doubles for read-only scenarios do not need
/// to implement mutation methods.
/// </summary>
public interface IAdminMediaWrite
{
    /// <summary>
    /// Applies a sparse <see cref="MediaPatch"/> to the media row identified by <paramref name="id"/>,
    /// enforcing optimistic concurrency via <paramref name="expectedVersion"/> — only non-null patch
    /// fields are written; absent fields are left unchanged. Returns the row's new <c>xmin</c> token and current
    /// <c>library_id</c>, both read straight from the UPDATE's <c>RETURNING</c> clause when the
    /// write succeeds (STORY-103; <c>LibraryId</c> added by SPEC F43.2, Epic V).
    /// <c>PATCH /api/media/{id}</c> uses this so every successful response can carry a fresh
    /// <c>ETag</c> — and, when relevant, the <c>X-Out-Of-Scope</c> warning — without a second read.
    ///
    /// The legacy <c>UpdateAsync</c> (plain <c>MediaWriteResult</c>, same parameters) was retired
    /// by gh-#4: it had zero production callers and survived only for STORY-039's original
    /// contract pin, which now pins THIS method instead.
    /// </summary>
    /// <param name="id">String representation of the media row id.</param>
    /// <param name="patch">Sparse field updates to apply.</param>
    /// <param name="expectedVersion">The row version the caller last observed; mismatches yield <see cref="MediaWriteResult.Conflict"/>.</param>
    /// <param name="scope">
    /// Retained for interface-shape stability (STORY-039's pinned 5-parameter contract); no longer
    /// used to gate the write. SPEC F43.2 (Epic V) repeals the source-row scope check — the write
    /// proceeds for any existing row regardless of its current library. The destination-out-of-scope
    /// warning on library reassignment (F20.6) is unrelated and unchanged; it is applied separately
    /// at the endpoint layer, not by this method.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct);

    /// <summary>
    /// Bulk-sets <c>eligible</c> on every row that matches <paramref name="filter"/> within the
    /// caller's <paramref name="scope"/>. Returns the number of rows affected.
    ///
    /// The filter selects exactly the same rows as <see cref="IAdminMediaQuery.ListAdminAsync"/> for
    /// the same filter — operators see what they are about to change before calling this method.
    ///
    /// Scope-bounding is mandatory: <c>library_id = ANY(@scope)</c> is always present; an empty scope
    /// short-circuits to 0 without issuing any SQL (default-deny). No identifier or string is ever
    /// interpolated into the SQL — all filter values are parameterized.
    /// </summary>
    /// <param name="filter">The same filter criteria used by the admin list view.</param>
    /// <param name="eligible">The eligibility value to write.</param>
    /// <param name="scope">Library access scope; empty scope → 0 rows affected, no SQL issued.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows whose <c>eligible</c> column was updated.</returns>
    Task<int> SetEligibilityAsync(
        MediaQuery filter,
        bool eligible,
        LibraryScope scope,
        CancellationToken ct);

    /// <summary>
    /// Bulk-reassigns every row matching <paramref name="filter"/> within <paramref name="scope"/>
    /// to <paramref name="toLibraryId"/>. Returns the number of rows updated.
    ///
    /// Returns <c>null</c> when <paramref name="toLibraryId"/> does not reference any existing
    /// <c>library.library</c> row — nothing is written in that case. The caller should surface
    /// this as a 400 response. The check happens before the scope guard so an unknown destination
    /// returns <c>null</c> even when the filter matches zero rows or the scope is empty.
    ///
    /// Scope-bounding is mandatory: <c>library_id = ANY(@scope)</c> is always present; an empty
    /// scope short-circuits to 0 without issuing any UPDATE SQL (default-deny). No filter value
    /// is ever concatenated into the SQL — all values are Npgsql parameters (same hygiene as F3).
    /// </summary>
    /// <param name="filter">The same filter criteria used by the admin list view.</param>
    /// <param name="toLibraryId">The destination library id to assign to every matching row.</param>
    /// <param name="scope">Library access scope; empty scope → 0 rows affected, no UPDATE issued.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows whose <c>library_id</c> was updated, or <c>null</c> if <paramref name="toLibraryId"/> is unknown.</returns>
    Task<int?> BulkReassignAsync(
        MediaQuery filter,
        long toLibraryId,
        LibraryScope scope,
        CancellationToken ct);
}
