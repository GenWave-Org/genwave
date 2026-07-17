namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Extends <see cref="MediaRow"/> with the <c>library_id</c> column for the unscoped object-level
/// authorization lookup (T042). The controller reads <c>LibraryId</c> to decide 403 vs 200 without
/// ever exposing the library_id in the response body.
/// </summary>
sealed class MediaRowWithLibrary : MediaRow
{
    /// <summary>The library this row belongs to; used for object-level scope check (T042).</summary>
    public long LibraryId { get; set; }
}
