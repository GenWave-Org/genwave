namespace GenWave.Host.Images;

/// <summary>
/// Outcome of <see cref="ImageNormalizeService.NormalizeAsync"/> (SPEC F128.6, STORY-333,
/// STORY-339, PLAN T291) — mirrors the house closed-hierarchy shape (e.g.
/// <c>GenWave.Tts.CrosstalkAssemblyResult</c>): a success always carries real, fully re-encoded
/// bytes; a failure always carries a reason, never both, never neither. The private constructor on
/// the abstract base closes the hierarchy so a caller's <c>switch</c> is exhaustive without a
/// discard arm. Nothing is ever written to any store on the <see cref="Failure"/> path — this type
/// is the seam T295/T307's write-path controllers branch on to decide whether to persist at all.
/// </summary>
public abstract record ImageNormalizeResult
{
    ImageNormalizeResult() { }

    /// <summary>A fresh 512×512 PNG, center-cropped and re-encoded — metadata (EXIF/GPS/text
    /// chunks) structurally absent, ready to hand straight to a store's write.</summary>
    /// <param name="Bytes">The normalized PNG bytes.</param>
    /// <param name="Sha256">The normalized bytes' hash, lowercase hex.</param>
    public sealed record Success(byte[] Bytes, string Sha256) : ImageNormalizeResult;

    /// <summary>The pipeline rejected the input at <see cref="Reason"/>'s gate — every case quiet
    /// 400s (the HTTP mapping itself belongs to T295/T307's controllers).</summary>
    public sealed record Failure(ImageNormalizeFailureReason Reason) : ImageNormalizeResult;
}
