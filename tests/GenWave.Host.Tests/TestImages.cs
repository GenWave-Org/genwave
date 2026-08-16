using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GenWave.Host.Tests;

/// <summary>
/// Generates small real PNG/JPEG fixtures — via the real ffmpeg binary, and via direct byte
/// construction for shapes ffmpeg itself won't produce (an oversized IHDR with no actual pixel
/// data behind it, a chunk carrying real text metadata) — for the SPEC F128.6 upload-pipeline
/// specs (PLAN T291). Mirrors <c>GenWave.MediaLibrary.Tests.TestMedia</c>'s own "exercise the real
/// binary, don't fake the bytes" idiom, image-flavored.
/// </summary>
static class TestImages
{
    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A real JPEG of the given size, via ffmpeg's <c>testsrc</c> lavfi source.</summary>
    public static byte[] CreateJpeg(int width, int height) => RunFfmpegToBytes(
        [
            "-f", "lavfi", "-i", $"testsrc=size={FormatSize(width, height)}:rate=1:duration=1",
            "-frames:v", "1", "-q:v", "2",
        ],
        "jpg");

    /// <summary>A real PNG of the given size, via ffmpeg's <c>testsrc</c> lavfi source.</summary>
    public static byte[] CreatePng(int width, int height) => RunFfmpegToBytes(
        [
            "-f", "lavfi", "-i", $"testsrc=size={FormatSize(width, height)}:rate=1:duration=1",
            "-frames:v", "1",
        ],
        "png");

    /// <summary>
    /// A real PNG three times as wide as it is tall: three solid <paramref name="bandWidth"/>-pixel
    /// square bands (<paramref name="leftColor"/> | <paramref name="midColor"/> |
    /// <paramref name="rightColor"/>) placed side by side, each <paramref name="bandWidth"/> ×
    /// <paramref name="bandWidth"/> pixels. Built so a CENTERED crop (side = min(iw,ih) =
    /// <paramref name="bandWidth"/>, x-offset = <paramref name="bandWidth"/>) lands EXACTLY on the
    /// middle band and discards the outer two entirely — a squash-then-crop mutant (scale the
    /// whole uncropped frame to the output square first, so the crop step becomes a no-op) instead
    /// leaves squashed-but-visible slivers of the outer bands at the output's own left/right edges,
    /// which is exactly what distinguishes the two implementations.
    /// </summary>
    public static byte[] CreateThreeBandPng(string leftColor, string midColor, string rightColor, int bandWidth) =>
        RunFfmpegToBytes(
            [
                "-f", "lavfi", "-i", $"color={leftColor}:size={FormatSize(bandWidth, bandWidth)}",
                "-f", "lavfi", "-i", $"color={midColor}:size={FormatSize(bandWidth, bandWidth)}",
                "-f", "lavfi", "-i", $"color={rightColor}:size={FormatSize(bandWidth, bandWidth)}",
                "-filter_complex", "[0:v][1:v][2:v]hstack=inputs=3",
                "-frames:v", "1",
            ],
            "png");

    /// <summary>
    /// A real animated PNG (APNG), two frames, via ffmpeg's own apng muxer — <c>acTL</c> genuinely
    /// precedes the first <c>IDAT</c> in the chunk stream (a real decoder-acceptable animation, not
    /// merely bytes shaped to look like one).
    /// </summary>
    public static byte[] CreateApng(int width, int height) => RunFfmpegToBytes(
        [
            "-f", "lavfi", "-i", $"testsrc=size={FormatSize(width, height)}:rate=2:duration=1",
            "-plays", "0",
        ],
        "apng");

    /// <summary>
    /// A real, high-entropy 16-bit-per-channel PNG (<c>rgba64be</c>) — the noise fill keeps it from
    /// compressing away, so the re-encoded output's byte size is representative of a real
    /// high-bit-depth photo rather than a flat test pattern (SPEC F128.1/<see
    /// cref="ImageNormalizeService.MaxOutputBytes"/>'s own repro: an un-pinned pix_fmt re-encode of
    /// this exact shape measured 1.78 MiB at 512×512).
    /// </summary>
    public static byte[] CreateHighBitDepthPng(int width, int height) => RunFfmpegToBytes(
        [
            "-f", "lavfi", "-i", $"nullsrc=size={FormatSize(width, height)}",
            "-filter_complex", "geq=random(1)*255:128:128",
            "-frames:v", "1", "-pix_fmt", "rgba64be",
        ],
        "png");

    /// <summary>
    /// A hand-built, minimal-but-valid PNG signature + IHDR chunk announcing
    /// <paramref name="width"/>x<paramref name="height"/> — no pixel data at all behind it. Used
    /// only to exercise the header-dimensions gate in isolation: real encoded pixel data for a
    /// multi-thousand-pixel-square image would be needlessly slow to generate, and the gate under
    /// test reads nothing past IHDR anyway.
    /// </summary>
    public static byte[] MakeSyntheticPngHeader(int width, int height)
    {
        var bytes = new byte[24];
        PngSignature.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }

    /// <summary>
    /// <paramref name="png"/> with a real, correctly-CRC'd <c>tEXt</c> chunk
    /// (<c>keyword\0text</c>) spliced in immediately after IHDR — a genuine text-metadata chunk a
    /// real decoder (ffmpeg included) reads back, not merely bytes that look like one.
    /// </summary>
    public static byte[] WithTextChunk(byte[] png, string keyword, string text)
    {
        var chunk = BuildChunk("tEXt", Encoding.Latin1.GetBytes($"{keyword}\0{text}"));

        // Splice point: signature(8) + IHDR chunk(8 header + 13 data + 4 crc) = 33.
        const int insertAt = 8 + 8 + 13 + 4;
        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, insertAt).CopyTo(result);
        chunk.CopyTo(result, insertAt);
        png.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + chunk.Length));
        return result;
    }

    /// <summary>
    /// A hand-built PNG whose signature + IHDR are genuinely well-formed and in-bounds (an 8-bit RGBA
    /// image at <paramref name="width"/>x<paramref name="height"/>), but whose single <c>IDAT</c> chunk
    /// carries random GARBAGE bytes instead of a real zlib/deflate stream — every gate that reads only
    /// the signature/IHDR/chunk-type-before-IDAT (SPEC F128.6's magic-bytes, header-dimensions, and
    /// APNG gates) admits this fixture exactly as it would a real photo; only ffmpeg's own decoder,
    /// asked to actually inflate the IDAT payload, rejects it with a non-zero exit. This is the T291
    /// round-2 reviewer's own "corrupt-but-header-valid PNG through the real binary" repro (PLAN T295
    /// rider) — the live case that reaches <see cref="GenWave.Host.Images.ImageNormalizeFailureReason.EncodeFailed"/>
    /// through <see cref="GenWave.Host.Images.FfmpegImageProcessRunner"/> for real, never a fake
    /// runner asserting the reason without ffmpeg ever actually running.
    /// </summary>
    public static byte[] CreateCorruptPng(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method

        var garbage = new byte[256];
        Random.Shared.NextBytes(garbage);

        var bytes = new List<byte>(PngSignature.Length + 64 + garbage.Length);
        bytes.AddRange(PngSignature);
        bytes.AddRange(BuildChunk("IHDR", ihdr));
        bytes.AddRange(BuildChunk("IDAT", garbage));
        bytes.AddRange(BuildChunk("IEND", []));
        return [.. bytes];
    }

    /// <summary>A length-prefixed, correctly-CRC'd PNG chunk (<c>length | type | data | crc32</c>) —
    /// the one chunk-framing primitive <see cref="WithTextChunk"/> and <see cref="CreateCorruptPng"/>
    /// both build on, so the CRC math lives in exactly one place.</summary>
    static byte[] BuildChunk(string type, byte[] data)
    {
        var chunk = new byte[4 + 4 + data.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        var crc = Crc32(chunk.AsSpan(4, 4 + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length, 4), crc);
        return chunk;
    }

    /// <summary>Every chunk type name present in <paramref name="png"/>, in stream order — a pure
    /// byte walk, no ffmpeg involved, so a spec can assert on it without a process round-trip.</summary>
    public static IReadOnlyList<string> PngChunkTypes(byte[] png)
    {
        var types = new List<string>();
        var offset = PngSignature.Length;
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            types.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
            offset += 8 + (int)length + 4;
        }

        return types;
    }

    static string FormatSize(int width, int height) =>
        $"{width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}";

    static byte[] RunFfmpegToBytes(IReadOnlyList<string> sourceArgs, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"genwave-testimg-{Guid.NewGuid():N}.{extension}");
        var args = new List<string> { "-nostats", "-hide_banner", "-loglevel", "error", "-y" };
        args.AddRange(sourceArgs);
        args.Add(path);

        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {stderr.Result}");

        try
        {
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The standard PNG/zlib CRC-32 (ISO 3309), per the PNG spec's own Annex D reference
    /// implementation — needed so <see cref="WithTextChunk"/> produces a chunk a real decoder
    /// accepts rather than rejects as corrupt.</summary>
    static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
