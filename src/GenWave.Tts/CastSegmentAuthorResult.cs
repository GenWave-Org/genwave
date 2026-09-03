namespace GenWave.Tts;

/// <summary>
/// Outcome of <see cref="CastSegmentAuthor.AuthorAsync"/> (SPEC F161.2, F161.3; STORY-391; PLAN T401)
/// — the <see cref="SafeSegmentAuthorResult"/> shape one sibling over. Success carries the newly
/// -inserted, now-eligible <c>library.media</c> row id; failure carries which stage aborted plus a
/// detail message safe to log. A third, unrepresentable state (both success and failure at once) is
/// ruled out by the private constructor — only the static factories below can create one.
/// </summary>
public sealed class CastSegmentAuthorResult
{
    readonly long? mediaId;
    readonly CastSegmentFailureReason reason;
    readonly string detail;

    CastSegmentAuthorResult(long? mediaId, CastSegmentFailureReason reason, string detail)
    {
        this.mediaId = mediaId;
        this.reason = reason;
        this.detail = detail;
    }

    public bool Succeeded => mediaId is not null;

    /// <summary>The inserted, now-eligible row id. Throws when read on a failed result.</summary>
    public long MediaId => mediaId
        ?? throw new InvalidOperationException($"Cannot read MediaId of a failed result: {reason} — {detail}");

    /// <summary>Which stage failed. Throws when read on a successful result.</summary>
    public CastSegmentFailureReason FailureReason => Succeeded
        ? throw new InvalidOperationException("Cannot read FailureReason of a successful result.")
        : reason;

    /// <summary>Detail message safe to log. Throws when read on a successful result.</summary>
    public string FailureDetail => Succeeded
        ? throw new InvalidOperationException("Cannot read FailureDetail of a successful result.")
        : detail;

    public static CastSegmentAuthorResult Success(long mediaId) =>
        new(mediaId, default, string.Empty);

    public static CastSegmentAuthorResult Failure(CastSegmentFailureReason reason, string detail) =>
        new(null, reason, detail);

    public override string ToString() =>
        Succeeded ? $"Success(MediaId={MediaId})" : $"Failure({FailureReason}: {FailureDetail})";
}
