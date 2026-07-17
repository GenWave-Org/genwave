namespace GenWave.Core.Domain;

/// <summary>
/// Shared outcome of a single-row rating write (SPEC F33.3, F33.4; STORY-109). Deliberately smaller
/// than <see cref="MediaWriteResult"/>: <see cref="Abstractions.IMediaRating"/> writes are never
/// scope-gated (F33.5) and carry no <c>expectedVersion</c> to conflict on (no <c>If-Match</c>
/// anywhere in this seam), so the only failure mode a vote or never-play set can hit is a missing row.
/// </summary>
public enum RatingWriteResult
{
    /// <summary>The media row exists and the write applied.</summary>
    Updated,

    /// <summary>No row with the given media id exists in <c>library.media</c>.</summary>
    NotFound,
}
