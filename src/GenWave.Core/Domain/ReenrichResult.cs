namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of a single-track re-enrichment schedule request (STORY-050, Epic J).
/// The enum shape keeps callers exhaustive-switch friendly and maps directly to HTTP
/// status codes in the endpoint layer (L6).
///
/// <para>
/// <c>OutOfScope</c> (track outside the caller's <see cref="LibraryScope"/> → 403) was retired by
/// SPEC F43.3 (Epic V, closes gitea-#203) — the same repeal as <see cref="MediaWriteResult"/>'s single-row
/// write path: scope no longer gates direct-by-id access to a single track.
/// </para>
/// </summary>
public enum ReenrichResult
{
    /// <summary>The track was found and has been queued for re-enrichment.</summary>
    Scheduled,

    /// <summary>No track with the given id exists in the database.</summary>
    NotFound,
}
