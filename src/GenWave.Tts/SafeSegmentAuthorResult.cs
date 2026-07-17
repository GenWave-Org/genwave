namespace GenWave.Tts;

/// <summary>
/// Outcome of <see cref="SafeSegmentAuthor.AuthorAsync"/> (SPEC F27.1, STORY-078). Success carries the
/// newly-inserted <c>library.media</c> row id; failure carries which stage aborted plus a detail
/// message that is safe to log — never the raw underlying exception (the P4 reviewer forward-note
/// "never let the raw PostgresException escape as the service's failure surface" applies to every
/// stage here, not just the insert). A third, unrepresentable state (both success and failure at
/// once) is ruled out by the private constructor — only the static factories below can create one.
/// </summary>
public sealed class SafeSegmentAuthorResult
{
    readonly long? mediaId;
    readonly SafeSegmentFailureReason reason;
    readonly string detail;

    SafeSegmentAuthorResult(long? mediaId, SafeSegmentFailureReason reason, string detail)
    {
        this.mediaId = mediaId;
        this.reason = reason;
        this.detail = detail;
    }

    public bool Succeeded => mediaId is not null;

    /// <summary>The inserted row id. Throws when read on a failed result.</summary>
    public long MediaId => mediaId
        ?? throw new InvalidOperationException($"Cannot read MediaId of a failed result: {reason} — {detail}");

    /// <summary>Which stage failed. Throws when read on a successful result.</summary>
    public SafeSegmentFailureReason FailureReason => Succeeded
        ? throw new InvalidOperationException("Cannot read FailureReason of a successful result.")
        : reason;

    /// <summary>Detail message safe to log. Throws when read on a successful result.</summary>
    public string FailureDetail => Succeeded
        ? throw new InvalidOperationException("Cannot read FailureDetail of a successful result.")
        : detail;

    public static SafeSegmentAuthorResult Success(long mediaId) =>
        new(mediaId, default, string.Empty);

    public static SafeSegmentAuthorResult Failure(SafeSegmentFailureReason reason, string detail) =>
        new(null, reason, detail);

    public override string ToString() =>
        Succeeded ? $"Success(MediaId={MediaId})" : $"Failure({FailureReason}: {FailureDetail})";
}
