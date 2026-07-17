namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/bulk/reassign</c> (L4 / STORY-049 bulk reassignment).
///
/// <see cref="ToLibraryId"/> is required — null or absent returns 400. An unknown library id
/// also returns 400 (pre-flight validated against <c>library.library</c> before any write occurs).
///
/// <see cref="Filter"/> is optional; a null or empty filter matches every in-scope source row.
/// Filter fields map one-to-one to the GET /api/media query parameters.
/// </summary>
public sealed record BulkReassignRequest(
    long? ToLibraryId,
    BulkReassignFilter? Filter);
