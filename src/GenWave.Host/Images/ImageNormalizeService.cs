using System.ComponentModel;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Images;

/// <summary>
/// The SPEC F128.6 upload pipeline (STORY-333, STORY-339, PLAN T291): bounded read → magic-bytes
/// gate → header-dimensions/APNG gate → ffmpeg center-crop-and-scale re-encode to a fresh 512×512
/// PNG. Every gate below runs BEFORE <see cref="IImageProcessRunner"/> is ever invoked — the
/// decompression-bomb class (SPEC F128.1's rejected-APNG rule included) dies at the header read,
/// never inside a decoder. Nothing is stored anywhere on any failure path: this service only ever
/// returns bytes in memory or a reason; persisting a <see cref="ImageNormalizeResult.Success"/> is
/// entirely T295/T307's write-path controllers' own job, not this seam's.
///
/// Consumed by <c>AvatarPackController</c> (T293) and <c>PersonaAvatarController</c> (T295);
/// <c>StationImageController</c> (T307) is the next consumer.
/// </summary>
public sealed class ImageNormalizeService(IImageProcessRunner processRunner, ILogger<ImageNormalizeService> logger)
{
    /// <summary>SPEC F128.6's own read cap — the caller (T295/T307) is expected to already hand
    /// over an at-most-this-large buffer; this gate re-asserts the cap rather than trusting that.</summary>
    public const int MaxInputBytes = 4 * 1024 * 1024;

    /// <summary>SPEC F128.6's minimum accepted axis, in pixels.</summary>
    public const int MinDimensionPx = 256;

    /// <summary>SPEC F128.6's maximum accepted axis, in pixels — the decompression-bomb ceiling.</summary>
    public const int MaxDimensionPx = 4096;

    /// <summary>The fixed output side length, in pixels (SPEC F128.6's "fresh 512×512 PNG").</summary>
    public const int OutputDimensionPx = 512;

    /// <summary>
    /// A defensive ceiling on the re-encoded output itself, matching SPEC F128.1's own catalog
    /// avatar cap (≤512 KiB per item) for the identical 512×512 asset shape — a high-bit-depth
    /// input (e.g. 16-bit-per-channel <c>rgba64be</c>) re-encoded without a pinned 8-bit
    /// <c>-pix_fmt</c> measured 1.78 MiB at this same output size, which would sit behind the
    /// immutable T295/T307 year-cache well past what the catalog itself would ever allow. The
    /// <c>-pix_fmt rgba</c> pin in <see cref="BuildFfmpegArgs"/> is the real fix; this check is
    /// belt-and-suspenders against anything that still slips past it.
    /// </summary>
    public const int MaxOutputBytes = 512 * 1024;

    /// <summary>
    /// A generous defensive ceiling on the ffmpeg re-encode itself, not a budget any legitimate
    /// normalize should approach — mirrors <c>ArtworkService.FfmpegTimeout</c>'s own reasoning:
    /// this runs off an authenticated but still request-supplied body, so a bounded worst case
    /// matters even though every byte reaching this point already passed the gates above.
    /// </summary>
    static readonly TimeSpan FfmpegTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the full SPEC F128.6 pipeline over <paramref name="bytes"/>. Every gate below is pure
    /// (no decoder, no ffmpeg) and runs strictly before <see cref="RunFfmpegNormalizeAsync"/> —
    /// the one stage that ever touches <see cref="IImageProcessRunner"/>.
    /// </summary>
    public async Task<ImageNormalizeResult> NormalizeAsync(byte[] bytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // Gate 1 — bounded read. Empty and oversize are kept as distinct reasons (rather than
        // both reading as TooLarge) so logs and the T295/T307 ProblemDetails mapping don't call
        // an empty upload "too large".
        if (bytes.Length == 0)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.Empty);
        if (bytes.Length > MaxInputBytes)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.TooLarge);

        // Gate 2 — magic bytes, BEFORE any decode. Content-Type is never consulted.
        var format = ImageMagicBytesGate.Detect(bytes);
        if (format is null)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.NotAnImage);

        // Gate 3 — header dimensions, still no decoder involved.
        var dimensionsRead = format == ImageFormat.Png
            ? PngImageHeader.TryReadDimensions(bytes, out var width, out var height)
            : JpegImageHeader.TryReadDimensions(bytes, out width, out height);

        if (!dimensionsRead)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.NotAnImage);

        // SPEC F128.1: an animated face must not slip in via upload either.
        if (format == ImageFormat.Png && PngImageHeader.HasAnimationChunk(bytes))
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.Animated);

        if (width < MinDimensionPx || height < MinDimensionPx)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.DimensionsTooSmall);

        if (width > MaxDimensionPx || height > MaxDimensionPx)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.DimensionsTooLarge);

        // Every gate above passed — only NOW does ffmpeg ever see the bytes.
        return await RunFfmpegNormalizeAsync(bytes, ct);
    }

    async Task<ImageNormalizeResult> RunFfmpegNormalizeAsync(byte[] bytes, CancellationToken ct)
    {
        // Temp files, not stdin/stdout piping (GenWave.Loudness.FfmpegProcess only redirects
        // stderr) — the same "real scratch file, deleted on every path" idiom
        // GenWave.Loudness.AubioBpmAnalyzer already uses for its own ffmpeg decode step. No
        // persistent cache backs this pipeline (unlike ArtworkService's disk cache): nothing here
        // is ever meant to survive past this one call.
        var inputPath = Path.Combine(Path.GetTempPath(), $"genwave-imgnorm-{Guid.NewGuid():N}.in");
        var outputPath = Path.Combine(Path.GetTempPath(), $"genwave-imgnorm-{Guid.NewGuid():N}.png");

        using var timeoutCts = new CancellationTokenSource(FfmpegTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            byte[] outputBytes;

            try
            {
                // linkedCts.Token, not ct alone, for the scratch write, the ffmpeg run, AND the
                // output read below — the 10s bounded-runtime ceiling this method promises must
                // cover all three, not merely the run in the middle (T295 review: the write used
                // to sit OUTSIDE this catch, then the output read did too — either one hitting the
                // timeout boundary escaped as an unhandled OperationCanceledException instead of
                // this pipeline's own quiet-400 contract; both now live inside this same try so the
                // `when (!ct.IsCancellationRequested)` filter below owns every timeout-boundary OCE
                // this method can produce).
                await File.WriteAllBytesAsync(inputPath, bytes, linkedCts.Token);
                await processRunner.RunAsync(BuildFfmpegArgs(inputPath, outputPath), linkedCts.Token);

                // The runner returning is not proof it produced anything usable — a fake/observing
                // IImageProcessRunner in tests writes nothing, and a real ffmpeg exiting zero
                // without writing outputPath is not a case this pipeline should ever hand to
                // Success. Verify BEFORE reading rather than letting File.ReadAllBytesAsync throw.
                if (!File.Exists(outputPath))
                {
                    logger.LogDebug("Image normalize ffmpeg run produced no output file.");
                    return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
                }

                outputBytes = await File.ReadAllBytesAsync(outputPath, linkedCts.Token);
                if (outputBytes.Length == 0 || !PngImageHeader.HasSignature(outputBytes))
                {
                    logger.LogDebug("Image normalize ffmpeg run produced no usable PNG output.");
                    return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
                }

                if (outputBytes.Length > MaxOutputBytes)
                {
                    logger.LogDebug(
                        "Image normalize output was {Bytes} bytes, over the {Cap} byte ceiling.",
                        outputBytes.Length, MaxOutputBytes);
                    return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own bounded-runtime timeout fired, not the caller's — surface as an
                // ordinary encode failure (mirrors ArtworkService.ExtractAsync's own split).
                // Covers a timeout anywhere above: the write, the ffmpeg run, or the output read.
                logger.LogDebug("Image normalize ffmpeg run exceeded {Timeout}.", FfmpegTimeout);
                return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
            }
            catch (InvalidOperationException ex)
            {
                // FfmpegProcess.RunFfmpegAsync's own failure shape: ffmpeg started but exited
                // non-zero.
                logger.LogDebug(ex, "Image normalize ffmpeg run failed.");
                return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
            }
            catch (Win32Exception ex)
            {
                // Process.Start's own failure shape when the ffmpeg binary itself cannot be
                // found/executed (the EspeakRespellOracle.DeriveAsync NativeErrorCode-triage
                // precedent) — without this catch, a missing binary would escape as an unhandled
                // 500 instead of this pipeline's own quiet-400 contract.
                logger.LogDebug(ex, "Image normalize ffmpeg failed to start.");
                return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.EncodeFailed);
            }

            var sha256 = Convert.ToHexStringLower(SHA256.HashData(outputBytes));
            return new ImageNormalizeResult.Success(outputBytes, sha256);
        }
        finally
        {
            DeleteIfExists(inputPath);
            DeleteIfExists(outputPath);
        }
    }

    /// <summary>
    /// Center-crops the larger axis down to a square (crop side = <c>min(iw,ih)</c>, offsets
    /// centered on each axis) then scales to an exact <see cref="OutputDimensionPx"/> square —
    /// SPEC F128.6's "center-crop + scale to a fresh 512×512 PNG". <c>-pix_fmt rgba</c> pins the
    /// output to 8-bit-per-channel regardless of the input's own bit depth — an unpinned
    /// high-bit-depth input (16-bit-per-channel <c>rgba64be</c>) re-encodes to a PNG several times
    /// larger at the same pixel dimensions, which <see cref="MaxOutputBytes"/>'s own remarks
    /// measure; 8-bit RGBA keeps transparency while bounding that growth. <c>-map_metadata -1</c>
    /// is explicit even though ffmpeg's PNG encoder already drops EXIF/text chunks by construction
    /// (verified empirically against the real binary) — the SPEC's own "regardless" instruction,
    /// belt-and-suspenders against a future ffmpeg build changing that default. <c>--</c> guards
    /// <paramref name="outputPath"/> the same way <c>FfmpegAudioMixer</c> guards its own output
    /// path, even though this one is always our own <see cref="Guid"/>-named temp file, never
    /// operator-influenced.
    /// </summary>
    static IReadOnlyList<string> BuildFfmpegArgs(string inputPath, string outputPath) =>
    [
        "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
        "-i", inputPath,
        "-frames:v", "1",
        "-vf",
        "crop='min(iw,ih)':'min(iw,ih)':'(iw-min(iw,ih))/2':'(ih-min(iw,ih))/2'," +
            $"scale={OutputDimensionPx}:{OutputDimensionPx}",
        "-pix_fmt", "rgba",
        "-map_metadata", "-1",
        "--",
        outputPath,
    ];

    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort scratch cleanup (ArtworkService's own precedent): a locked/undeletable
            // temp file is not worth failing — or masking — an otherwise-resolved call over.
        }
    }
}
