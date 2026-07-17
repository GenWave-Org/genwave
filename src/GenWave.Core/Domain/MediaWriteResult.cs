namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of a catalog write operation (STORY-039, Epic I).
/// The enum shape keeps callers exhaustive-switch friendly and maps directly to HTTP status codes in W2.
///
/// <para>
/// <c>OutOfScope</c> (source row outside the caller's <see cref="LibraryScope"/> → 403) was retired
/// by SPEC F43.2 (Epic V, closes gitea-#203): scope is a curation filter, not an access gate, so a
/// single-row write no longer inspects the row's current library against scope. The destination-scope
/// warning on library reassignment (F20.6) is unrelated and unchanged — see
/// <see cref="Abstractions.IAdminMediaWrite.UpdateReturningVersionAsync"/>.
/// </para>
/// </summary>
public enum MediaWriteResult
{
    /// <summary>The row was found, version matched, and the patch was applied.</summary>
    Updated,

    /// <summary>The row was found but the provided <c>expectedVersion</c> did not match the current version (optimistic-concurrency conflict).</summary>
    Conflict,

    /// <summary>No row with the given id exists in the database.</summary>
    NotFound,

    /// <summary>
    /// The patch specified a <c>library_id</c> that references no row in <c>library.library</c>.
    /// The write was rejected (FK violation — no row was changed).
    /// </summary>
    UnknownLibraryId,
}
