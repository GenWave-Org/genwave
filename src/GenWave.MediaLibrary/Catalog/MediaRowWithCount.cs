namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Extends <see cref="MediaRow"/> with the <c>total_count</c> window-function column produced by
/// <c>COUNT(*) OVER()</c> in the paged list query (T041). Using a subclass avoids duplicating all
/// the projection properties while keeping one Dapper type per query shape.
/// </summary>
sealed class MediaRowWithCount : MediaRow
{
    /// <summary>Total matching rows across all pages, as returned by <c>COUNT(*) OVER()</c>.</summary>
    public int TotalCount { get; set; }
}
