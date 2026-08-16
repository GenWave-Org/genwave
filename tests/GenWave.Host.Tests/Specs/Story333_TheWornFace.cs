// STORY-333 — The worn face (SPEC F128.5/.6/.9 · PLAN T291 pipeline + T295 endpoints)
//
// BDD specification — xUnit. The normalize pipeline's own gates pin to T291; the
// endpoints pin to T295. The Personas-page render/placeholder/offer UI (AC2/AC4's UI
// halves) lives in admin-ui jest (persona-faces.spec.tsx) + the T301 wire.
//
// T291's five facts below run against the REAL ImageNormalizeService/FfmpegImageProcessRunner —
// real ffmpeg, real generated PNG/JPEG fixtures (TestImages, mirrors GenWave.MediaLibrary.Tests'
// own TestMedia idiom) — never a mock of the pipeline itself. The two "before ffmpeg" gate facts
// substitute CountingImageProcessRunner in IImageProcessRunner's place instead: proof that a
// rejected input never reached ffmpeg at all, not merely that the right failure reason came back.

namespace GenWave.Host.Tests.Specs;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Images;
using GenWave.Host.Tests.Fakes;

public static class FeatureTheWornFace
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the pipeline (T291)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheNormalizePipelineProducesACleanFace
    {
        static ImageNormalizeService BuildRealService() =>
            new(new FfmpegImageProcessRunner(), NullLogger<ImageNormalizeService>.Instance);

        [Fact]
        public async Task OutputIsAFresh512SquarePng()
        {
            // Given a real, plausibly-sized, non-square JPEG (any accepted input)...
            var input = TestImages.CreateJpeg(400, 300);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then a fresh 512×512 PNG results — probed independently via ffprobe, not by
            // trusting the service's own OutputDimensionPx constant back at itself.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var (width, height, codec) = await ProbePngAsync(success.Bytes);
            Assert.Equal("png", codec);
            Assert.Equal(512, width);
            Assert.Equal(512, height);
        }

        [Fact]
        public async Task NonSquareInputIsCenterCropped()
        {
            // Given a 900×300 PNG: three solid 300×300 bands side by side, red | green | blue.
            // min(iw,ih) = 300, so a CORRECT center crop takes x ∈ [300,600) — the middle band
            // ONLY — and discards the outer red/blue bands entirely before ever scaling. This
            // fixture kills the scale-then-crop mutant (scale the whole 900×300 frame down to
            // 512×512 FIRST, which squashes rather than discards the outer bands, then a
            // now-square crop is a no-op): under the mutant, the squashed red/blue bands are still
            // visible at the output's own left/right edges, whereas the correct crop-then-scale
            // pipeline is uniformly green edge to edge, no red or blue anywhere.
            var input = TestImages.CreateThreeBandPng("red", "green", "blue", bandWidth: 300);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the ENTIRE output reads green — center same as both edges — proof the crop
            // discarded the outer bands before scaling, not merely that a mid-frame sample happens
            // to land on green (which a squash alone would already satisfy).
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var centerPixel = await SamplePixelAsync(success.Bytes, x: 256, y: 256);
            var leftEdgePixel = await SamplePixelAsync(success.Bytes, x: 10, y: 256);
            var rightEdgePixel = await SamplePixelAsync(success.Bytes, x: 502, y: 256);

            Assert.True(centerPixel.G > centerPixel.R && centerPixel.G > centerPixel.B,
                $"expected a green center pixel, got {centerPixel}");
            Assert.True(leftEdgePixel.G > leftEdgePixel.R,
                $"expected the left edge to be green (red band discarded, not squashed-in), got {leftEdgePixel}");
            Assert.True(rightEdgePixel.G > rightEdgePixel.B,
                $"expected the right edge to be green (blue band discarded, not squashed-in), got {rightEdgePixel}");
        }

        [Fact]
        public async Task MetadataIsStructurallyAbsentFromTheOutput()
        {
            // Given a PNG carrying a real, correctly-CRC'd tEXt chunk — self-verified here so a
            // broken fixture (one that never actually carried metadata) can't pass this fact for
            // the wrong reason...
            var plain = TestImages.CreatePng(400, 400);
            var input = TestImages.WithTextChunk(plain, "Comment", "secret gps location data");
            Assert.Contains("tEXt", TestImages.PngChunkTypes(input));

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the output carries no text/EXIF chunk at all — EXIF/GPS/text chunks in the
            // input do not survive the re-encode.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var outputChunkTypes = TestImages.PngChunkTypes(success.Bytes);
            Assert.DoesNotContain("tEXt", outputChunkTypes);
            Assert.DoesNotContain("iTXt", outputChunkTypes);
            Assert.DoesNotContain("zTXt", outputChunkTypes);
            Assert.DoesNotContain("eXIf", outputChunkTypes);
        }

        [Fact]
        public async Task HighBitDepthInputLandsUnderTheOutputCeiling()
        {
            // Given a real, high-entropy 16-bit-per-channel (rgba64be) PNG — noise-filled so it
            // doesn't compress away, the exact shape whose un-pinned re-encode measured 1.78 MiB
            // at 512×512 (ImageNormalizeService.MaxOutputBytes's own remarks)...
            var input = TestImages.CreateHighBitDepthPng(600, 600);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the output still succeeds AND lands at or under SPEC F128.1's own ≤512 KiB
            // catalog-avatar cap — proof the -pix_fmt rgba pin (not merely the defensive ceiling
            // check) keeps a high-bit-depth input's output bounded.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            Assert.True(success.Bytes.Length <= ImageNormalizeService.MaxOutputBytes,
                $"expected <= {ImageNormalizeService.MaxOutputBytes} bytes, got {success.Bytes.Length}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write paths (T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioWearingAPackFaceCopiesIt
    {
        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void TheFaceRowIsACopyWithCatalogProvenance()
        {
            // from-pack → persona_avatar(source='catalog', imported_from=pack slug).
            Assert.Fail("pending T295");
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void TheTokenRotatesOnTheWrite()
        {
            Assert.Fail("pending T295");
        }
    }

    public sealed class ScenarioOwnerUploadWearsAndRemoves
    {
        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void PutStoresTheNormalizedFaceWithSourceUpload()
        {
            Assert.Fail("pending T295");
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void DeleteRemovesTheRow()
        {
            Assert.Fail("pending T295");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — hostile input dies at the gate (T291/T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioHostileUploadsDieQuietlyAtTheGates
    {
        [Fact]
        public async Task ANonImageBodyFailsTheMagicGateBeforeAnyDecoderRuns()
        {
            // Given a body that is neither PNG nor JPEG by signature...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var notAnImage = "this is definitely not an image"u8.ToArray();

            // When it is normalized...
            var result = await service.NormalizeAsync(notAnImage, CancellationToken.None);

            // Then it is rejected at the magic-bytes gate, and ffmpeg is NEVER invoked — proof the
            // gate runs BEFORE any decoder touches the bytes, not merely that the right reason
            // came back.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.NotAnImage, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task OversizeDimensionsFailTheHeaderGateBeforeFfmpeg()
        {
            // Given a synthetic PNG header (signature + IHDR only, no pixel data at all) announcing
            // dimensions on either side of the SPEC F128.6 bounds...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var oversized = TestImages.MakeSyntheticPngHeader(width: 5000, height: 5000);
            var undersized = TestImages.MakeSyntheticPngHeader(width: 128, height: 128);

            // When each is normalized...
            var oversizedResult = await service.NormalizeAsync(oversized, CancellationToken.None);
            var undersizedResult = await service.NormalizeAsync(undersized, CancellationToken.None);

            // Then both are rejected at the header-dimensions gate — >4096px (the
            // decompression-bomb class) and <256px alike — and ffmpeg is NEVER invoked for either:
            // the gate reads IHDR alone, no decoder ever sees these bytes.
            var oversizedFailure = Assert.IsType<ImageNormalizeResult.Failure>(oversizedResult);
            Assert.Equal(ImageNormalizeFailureReason.DimensionsTooLarge, oversizedFailure.Reason);
            var undersizedFailure = Assert.IsType<ImageNormalizeResult.Failure>(undersizedResult);
            Assert.Equal(ImageNormalizeFailureReason.DimensionsTooSmall, undersizedFailure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task AnimatedApngFailsTheAnimationGateBeforeFfmpeg()
        {
            // Given a real APNG (ffmpeg's own apng muxer) — self-verified here so a broken
            // fixture (one that never actually carried acTL) can't pass this fact for the wrong
            // reason...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var apng = TestImages.CreateApng(300, 300);
            Assert.Contains("acTL", TestImages.PngChunkTypes(apng));

            // When it is normalized...
            var result = await service.NormalizeAsync(apng, CancellationToken.None);

            // Then it is rejected as Animated, and ffmpeg is NEVER invoked — SPEC F128.1's
            // "acTL rejected" rule holds at the upload gate too, before any decoder runs.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.Animated, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task OversizeBodyFailsTheBoundedReadGateBeforeFfmpeg()
        {
            // Given a body one byte over ImageNormalizeService.MaxInputBytes...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var oversizeBody = new byte[ImageNormalizeService.MaxInputBytes + 1];

            // When it is normalized...
            var result = await service.NormalizeAsync(oversizeBody, CancellationToken.None);

            // Then it is rejected as TooLarge, and ffmpeg is NEVER invoked — the bounded-read gate
            // runs before even the magic-bytes check.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.TooLarge, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task EmptyBodyFailsTheBoundedReadGateBeforeFfmpegWithItsOwnReason()
        {
            // Given an empty body...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);

            // When it is normalized...
            var result = await service.NormalizeAsync([], CancellationToken.None);

            // Then it is rejected as Empty — distinct from TooLarge, so an empty upload never
            // misreads as an oversize one in logs or the T295/T307 ProblemDetails mapping — and
            // ffmpeg is NEVER invoked.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.Empty, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void ADecodeFailureLeavesThePreviousFaceUnchanged()
        {
            Assert.Fail("pending T295");
        }
    }

    // ── ffprobe/ffmpeg verification helpers — black-box: probe the produced bytes, never the
    // service's internals (mirrors GenWave.Tts.Tests' own Story327_TwoVoicesOneClip idiom). ──────

    static async Task<(int Width, int Height, string Codec)> ProbePngAsync(byte[] pngBytes)
    {
        var path = await WriteTempFileAsync(pngBytes);
        try
        {
            var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("stream=width,height,codec_name");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1");
            psi.ArgumentList.Add(path);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            int? width = null;
            int? height = null;
            string? codec = null;
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0])
                {
                    case "width": width = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "height": height = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "codec_name": codec = parts[1]; break;
                }
            }

            if (width is null || height is null || codec is null)
                throw new InvalidOperationException($"ffprobe produced no usable stream info: {stdout}");

            return (width.Value, height.Value, codec);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Reads back the single RGB pixel at (<paramref name="x"/>, <paramref name="y"/>) via
    /// a 1×1 ffmpeg crop piped to raw stdout — the same "shell to the real binary, parse its
    /// output" idiom <c>Story327_TwoVoicesOneClip.ProbeFlatFactorAsync</c> already uses.</summary>
    static async Task<(byte R, byte G, byte B)> SamplePixelAsync(byte[] pngBytes, int x, int y)
    {
        var path = await WriteTempFileAsync(pngBytes);
        try
        {
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(
                $"crop=1:1:{x.ToString(CultureInfo.InvariantCulture)}:{y.ToString(CultureInfo.InvariantCulture)}");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("rawvideo");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("rgb24");
            psi.ArgumentList.Add("-");

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stdoutTask = ReadExactlyAsync(p.StandardOutput.BaseStream, 3);
            await p.StandardError.ReadToEndAsync();
            var pixel = await stdoutTask;
            await p.WaitForExitAsync();

            return (pixel[0], pixel[1], pixel[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (n == 0) break;
            read += n;
        }

        return buffer;
    }

    static async Task<string> WriteTempFileAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"genwave-story333-probe-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}
