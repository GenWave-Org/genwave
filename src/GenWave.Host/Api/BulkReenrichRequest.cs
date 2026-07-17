namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/bulk/reenrich</c> (STORY-051 bulk re-enrichment).
///
/// <para>
/// <see cref="Fields"/> is an array of field token strings: valid values are
/// <c>cue</c>, <c>energy</c>, <c>loudness</c>, <c>tags</c>, <c>all</c> (case-insensitive).
/// Null or an empty array normalizes to <c>all</c> (reset all four groups). An unknown token
/// returns 400 with no rows written.
/// </para>
///
/// <para>
/// <see cref="Filter"/> is optional; a null or empty filter matches every in-scope row.
/// Filter fields map one-to-one to the <c>GET /api/media</c> query parameters.
/// </para>
/// </summary>
public sealed record BulkReenrichRequest(
    BulkReenrichFilter? Filter,
    IReadOnlyList<string>? Fields);
