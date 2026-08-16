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
/// <para>
/// Consumed by <c>AvatarPackController</c> (T293), <c>PersonaAvatarController</c> (T295),
/// <c>StationImageController</c> (T307), and <c>CatalogPersonaAvatarInstaller</c> (T297) via
/// <see cref="NormalizeAsync"/> — the general upload path, ALWAYS ffmpeg-re-encoded, since an
/// arbitrary uploaded body is never trusted enough to skip pixel re-compression. The two catalog
/// INSTALL callers (<c>AvatarPackController</c>, <c>CatalogPersonaAvatarInstaller</c>) instead call
/// <see cref="NormalizeCatalogAssetAsync"/> (gh-#520): the SAME gates, but a catalog-sourced item
/// that is already exactly <see cref="OutputDimensionPx"/>-square takes the
/// <see cref="PngMetadataStripper"/> fast path — a chunk-level metadata strip, never a pixel
/// re-encode — instead of paying ffmpeg's own (measurably weaker) PNG compression a second time over
/// pixels nobody asked to change. See that method's own remarks for the full gh-#520 reasoning.
/// </para>
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
    /// A defensive ceiling on the ffmpeg-RE-ENCODED output itself — gh-#520's own honest rewrite of
    /// what this number actually bounds. The OLD 512 KiB figure was never this pipeline's own
    /// number: it was SPEC F128.1's catalog INPUT-curation cap (≤512 KiB per item, enforced at
    /// catalog CI publish time, over ImageMagick max-compression output), borrowed here as an OUTPUT
    /// bound for the identical 512×512 shape — a coincidence of matching dimensions, not a matching
    /// encoder. ffmpeg's own PNG encoder is measurably weaker than ImageMagick's: a real, gates-
    /// passing 512×512 catalog seed (460–512 KiB under ImageMagick) re-encoded to 532–663 KiB even at
    /// ffmpeg's OWN best settings — busting the borrowed 512 KiB ceiling on every legitimate catalog
    /// install (the gh-#520 bug report). 768 KiB is THIS pipeline's own honest number instead:
    /// measured real-art re-encodes at max settings (<see cref="BuildFfmpegArgs"/>'s own
    /// <c>-compression_level 100 -pred mixed</c>) top out around 541 KiB, so 768 KiB bounds the
    /// actually-served weight with real headroom, while staying nowhere near the multi-MiB
    /// high-bit-depth pathology this ceiling ALSO still guards against (see below) — the catalog's
    /// OWN ≤512 KiB distribution bar (SPEC F128.1) is unchanged and entirely unrelated to this
    /// constant; a catalog install itself no longer even reaches this ceiling at all for an exactly
    /// 512×512 item, since <see cref="NormalizeCatalogAssetAsync"/>'s own <see cref="PngMetadataStripper"/>
    /// fast path never re-encodes pixels in the first place.
    /// <para>
    /// Still belt-and-suspenders against the high-bit-depth pathology this ceiling was ORIGINALLY
    /// added for: an input (e.g. 16-bit-per-channel <c>rgba64be</c>) re-encoded without a pinned
    /// 8-bit <c>-pix_fmt</c> measured 1.78 MiB at this same output size. The <c>-pix_fmt rgba</c> pin
    /// in <see cref="BuildFfmpegArgs"/> is the real fix; this check remains the defensive backstop
    /// against anything that still slips past it.
    /// </para>
    /// <para>
    /// For <see cref="NormalizeCatalogAssetAsync"/>'s own fast path specifically, the bound that
    /// actually does the work in practice is upstream of this one:
    /// <see cref="GenWave.Host.Catalog.CatalogIndexValidator.MaxPngAssetBytes"/> (512 KiB, enforced at
    /// fetch) already caps whatever the stripper can ever be handed, and the stripper only ever
    /// removes bytes — this ceiling is still asserted directly at that fast path too (fix round
    /// finding #4), since bounding what gets STORED is THIS constant's own job, never a promise this
    /// pipeline should trust an unrelated caller's cap to keep on its behalf.
    /// </para>
    /// </summary>
    public const int MaxOutputBytes = 768 * 1024;

    /// <summary>
    /// A generous defensive ceiling on the ffmpeg re-encode itself, not a budget any legitimate
    /// normalize should approach — mirrors <c>ArtworkService.FfmpegTimeout</c>'s own reasoning:
    /// this runs off an authenticated but still request-supplied body, so a bounded worst case
    /// matters even though every byte reaching this point already passed the gates above.
    /// </summary>
    static readonly TimeSpan FfmpegTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the full SPEC F128.6 pipeline over <paramref name="bytes"/> for the GENERAL upload path
    /// (an arbitrary caller-supplied body — <c>PersonaAvatarController.Put</c>,
    /// <c>StationImageController.Put</c>) — every gate is pure (no decoder, no ffmpeg) and runs
    /// strictly before <see cref="RunFfmpegNormalizeAsync"/>, and the pixel re-encode always runs:
    /// an arbitrary upload is never trusted enough to skip it. <see cref="NormalizeCatalogAssetAsync"/>
    /// is the catalog-INSTALL sibling that CAN skip it, for the narrower "already-512×512,
    /// catalog-sourced" case — see that method's own remarks.
    /// </summary>
    public async Task<ImageNormalizeResult> NormalizeAsync(byte[] bytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (RunPreDecodeGates(bytes, out _, out _, out _) is { } failure)
            return failure;

        // Every gate above passed — only NOW does ffmpeg ever see the bytes.
        return await RunFfmpegNormalizeAsync(bytes, ct);
    }

    /// <summary>
    /// gh-#520's fast path: the SAME pure pre-decode gates <see cref="NormalizeAsync"/> runs (bounded
    /// read → magic bytes → header dimensions/APNG → min/max dimensions — re-asserted here rather
    /// than trusted from the caller, since the catalog's own CI having approved this PNG at publish
    /// time proves nothing about what THIS fetch actually returned, per
    /// <c>AvatarPackController</c>/<c>CatalogPersonaAvatarInstaller</c>'s own RE-VALIDATION IS NOT
    /// OPTIONAL remarks), but with ONE extra branch after them: a PNG that is already exactly
    /// <see cref="OutputDimensionPx"/>×<see cref="OutputDimensionPx"/> — the shape EVERY catalog
    /// avatar seed is required to already be — never needs a pixel re-encode AT ALL, only its
    /// metadata stripped. <see cref="PngMetadataStripper.TryStrip"/> does exactly that: a
    /// CRC-preserving, output-≤-input-by-construction chunk filter, never touching ffmpeg.
    ///
    /// <para>
    /// <b>WHY THIS METHOD EXISTS, NOT MERELY A BRANCH INSIDE <see cref="NormalizeAsync"/>.</b> The
    /// fast path is scoped to CATALOG-SOURCED content on purpose — a hash-verified fetch from a
    /// CI-gated origin that ALSO already re-asserts shape/animation/magic-bytes here, never a wholly
    /// untrusted, arbitrary caller upload. <see cref="NormalizeAsync"/>'s own callers (an owner's raw
    /// PUT body) get no such prior gate anywhere upstream, so they always pay for the full re-encode —
    /// keeping the two call shapes as two named methods, rather than one method silently branching on
    /// "trust level," makes that boundary a caller CHOICE at the type level, not an implicit runtime
    /// inference this class would otherwise have to get right on every future caller's behalf.
    /// </para>
    ///
    /// <para>
    /// <b>FALLBACK, NOT FAILURE.</b> Any reason the fast path cannot confidently apply — the item is
    /// not a PNG, is not exactly 512×512 (defensive: catalog CI is SUPPOSED to guarantee this, but
    /// this method never trusts that alone), <see cref="PngMetadataStripper.TryStrip"/> itself reports
    /// a walk it could not confidently complete, or the walk DID complete but the stripped output is
    /// still over <see cref="MaxOutputBytes"/> (fix round finding #4 — belt-and-suspenders: the
    /// catalog fetch's own per-asset cap already bounds this in practice today, since the stripper
    /// only ever removes bytes, but <see cref="MaxOutputBytes"/>'s own job is bounding what actually
    /// gets STORED, so it is asserted here directly rather than merely trusted from an unrelated
    /// caller's cap) — falls straight through to the SAME <see cref="RunFfmpegNormalizeAsync"/> the
    /// general upload path uses, never a distinct failure reason of its own. The fast path is purely
    /// an optimization; its absence changes nothing about what ends up stored, only how many ffmpeg
    /// invocations it took to get there.
    /// </para>
    /// </summary>
    public async Task<ImageNormalizeResult> NormalizeCatalogAssetAsync(byte[] bytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (RunPreDecodeGates(bytes, out var format, out var width, out var height) is { } failure)
            return failure;

        if (format == ImageFormat.Png && width == OutputDimensionPx && height == OutputDimensionPx
            && PngMetadataStripper.TryStrip(bytes, out var stripped) && stripped.Length <= MaxOutputBytes)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(stripped));
            return new ImageNormalizeResult.Success(stripped, sha256);
        }

        return await RunFfmpegNormalizeAsync(bytes, ct);
    }

    /// <summary>
    /// The pure, pre-decode gate sequence shared by <see cref="NormalizeAsync"/> and
    /// <see cref="NormalizeCatalogAssetAsync"/> — bounded read → magic bytes → header dimensions/APNG
    /// → min/max dimensions, never touching a decoder. Returns the specific
    /// <see cref="ImageNormalizeResult.Failure"/> the caller should return immediately, or
    /// <see langword="null"/> when every gate passed (with <paramref name="format"/>/
    /// <paramref name="width"/>/<paramref name="height"/> then holding the detected shape for the
    /// caller's own use).
    /// </summary>
    static ImageNormalizeResult.Failure? RunPreDecodeGates(
        byte[] bytes, out ImageFormat format, out int width, out int height)
    {
        format = default;
        width = 0;
        height = 0;

        // Gate 1 — bounded read. Empty and oversize are kept as distinct reasons (rather than
        // both reading as TooLarge) so logs and the T295/T307 ProblemDetails mapping don't call
        // an empty upload "too large".
        if (bytes.Length == 0)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.Empty);
        if (bytes.Length > MaxInputBytes)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.TooLarge);

        // Gate 2 — magic bytes, BEFORE any decode. Content-Type is never consulted.
        var detected = ImageMagicBytesGate.Detect(bytes);
        if (detected is null)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.NotAnImage);
        format = detected.Value;

        // Gate 3 — header dimensions, still no decoder involved.
        var dimensionsRead = format == ImageFormat.Png
            ? PngImageHeader.TryReadDimensions(bytes, out width, out height)
            : JpegImageHeader.TryReadDimensions(bytes, out width, out height);

        if (!dimensionsRead)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.NotAnImage);

        // SPEC F128.1: an animated face must not slip in via upload OR catalog install either.
        if (format == ImageFormat.Png && PngImageHeader.HasAnimationChunk(bytes))
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.Animated);

        if (width < MinDimensionPx || height < MinDimensionPx)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.DimensionsTooSmall);

        if (width > MaxDimensionPx || height > MaxDimensionPx)
            return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.DimensionsTooLarge);

        return null;
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
                    // gh-#520: a genuinely SUCCESSFUL re-encode that merely came out too big is a
                    // different, honestly-nameable claim than EncodeFailed's "ffmpeg itself failed" —
                    // OutputTooLarge, never EncodeFailed, is what ImageNormalizeProblemMapper maps to
                    // its own truthful "too large to store" copy.
                    logger.LogDebug(
                        "Image normalize output was {Bytes} bytes, over the {Cap} byte ceiling.",
                        outputBytes.Length, MaxOutputBytes);
                    return new ImageNormalizeResult.Failure(ImageNormalizeFailureReason.OutputTooLarge);
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
    /// belt-and-suspenders against a future ffmpeg build changing that default.
    /// <c>-compression_level 100 -pred mixed</c> (gh-#520) push the PNG encoder to its OWN maximum
    /// effort — the "mixed" adaptive filter selection, the same class of per-scanline filter choice
    /// ImageMagick's own max-compression already makes, rather than ffmpeg's `none` default — closing
    /// most (never all — a different encoder is still a different encoder) of the measured ~30% gap
    /// against the ImageMagick max-compression that produced the catalog's own seeds;
    /// <see cref="MaxOutputBytes"/>'s own remarks carry the exact measured numbers this raised the
    /// ceiling to accommodate. <c>--</c> guards <paramref name="outputPath"/> the same way
    /// <c>FfmpegAudioMixer</c> guards its own output path, even though this one is always our own
    /// <see cref="Guid"/>-named temp file, never operator-influenced.
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
        "-compression_level", "100",
        "-pred", "mixed",
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
