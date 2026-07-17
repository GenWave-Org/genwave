namespace GenWave.Tts;

/// <summary>
/// Seam over <see cref="SafeSegmentAuthor"/> so callers across the Host boundary — the
/// <c>POST /api/safe-segments</c> endpoint (P6) and the boot seed (P7) — can be exercised in
/// controller/hosted-service unit tests with a fake, without spinning up the real TTS/mixer/analyzer
/// pipeline. <see cref="SafeSegmentAuthor"/> remains the sole production implementation.
/// </summary>
public interface ISafeSegmentAuthor
{
    /// <inheritdoc cref="SafeSegmentAuthor.AuthorAsync"/>
    Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct);
}
