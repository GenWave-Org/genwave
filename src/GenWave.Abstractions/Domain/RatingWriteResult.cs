namespace GenWave.Core.Domain;

/// <summary>
/// Shared outcome of a single-row rating write (SPEC F33.3, F33.4; STORY-109). Deliberately smaller
/// than <see cref="MediaWriteResult"/>: <see cref="Abstractions.IMediaRating"/> writes are never
/// MAIN-scope-gated (F33.5) and carry no <c>expectedVersion</c> to conflict on (no <c>If-Match</c>
/// anywhere in this seam), so a vote or never-play set can only fail on a missing row — or, since
/// gh-#99, on targeting safe-scope content.
/// </summary>
public enum RatingWriteResult
{
    /// <summary>The media row exists and the write applied.</summary>
    Updated,

    /// <summary>No row with the given media id exists in <c>library.media</c>.</summary>
    NotFound,

    /// <summary>
    /// gh-#99 — the row exists but lives in a <c>Station:SafeScope:LibraryIds</c> library: safe-loop
    /// tracks and station IDs are functional audio, never rateable, and a never-play write against
    /// them could silence the never-silent fallback itself. Nothing was written.
    /// </summary>
    SafeContentExcluded,
}
