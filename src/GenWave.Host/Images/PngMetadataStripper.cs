using System.Buffers.Binary;

namespace GenWave.Host.Images;

/// <summary>
/// The gh-#520 hotfix's own fast path for a catalog-sourced, already-512×512 PNG (SPEC F128.6
/// amendment, PLAN T293/T297): walks the chunk stream exactly the way <see cref="PngImageHeader"/>'s
/// own <see cref="PngImageHeader.HasAnimationChunk"/> already does — bounded, monotonic, fail-closed
/// on anything malformed rather than throwing or emitting partial output — and keeps ONLY the
/// critical chunks (<c>IHDR</c>/<c>PLTE</c>/<c>IDAT</c>/<c>IEND</c>) plus <c>tRNS</c> (the one
/// ancillary chunk that carries real pixel data — per-palette-entry or per-channel transparency —
/// rather than metadata). Every other chunk type (<c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c>/<c>eXIf</c>/
/// <c>tIME</c>/<c>pHYs</c>/<c>gAMA</c>/<c>iCCP</c>/<c>acTL</c>/<c>fcTL</c>/<c>fdAT</c>/anything this
/// type does not explicitly recognize) is dropped.
///
/// <para>
/// <b>WHY THIS EXISTS (gh-#520).</b> <see cref="ImageNormalizeService"/>'s ffmpeg PNG encoder is
/// measurably ~30% weaker than the ImageMagick max-compression that produced the Community Catalog's
/// own avatar seeds — re-encoding a catalog item that already gates-passed at 460–512 KiB could push
/// it to 532–663 KiB, busting <see cref="ImageNormalizeService.MaxOutputBytes"/> and failing EVERY
/// catalog avatar-pack install. A catalog-sourced item that already IS exactly 512×512 needs no pixel
/// re-encode at all — <see cref="ImageNormalizeService"/>'s own re-validation exists to strip
/// metadata and enforce shape, not to re-compress pixels nobody asked to change — so this class does
/// exactly that ONE job, byte-for-byte, never touching a single pixel.
/// </para>
///
/// <para>
/// <b>OUTPUT ≤ INPUT, BY CONSTRUCTION.</b> Every byte <see cref="TryStrip"/> ever writes is a VERBATIM
/// slice of <paramref name="input"/> — the 8-byte signature, plus zero or more whole chunks copied
/// unmodified, CRC included (a kept chunk's own CRC was already computed over these same
/// never-touched bytes, so it stays UNCHANGED — this class never computes, recomputes, or verifies a
/// CRC of its own; it only ever copies whichever bytes were already there). Nothing is ever added, so
/// the stripped output can never exceed the input's own length; it is at most reduced.
/// </para>
///
/// <para>
/// <b>SPEC F129.2's "served bytes are metadata-free by construction" claim holds through this path
/// too.</b> A caller that has ALREADY re-asserted the input is a well-formed, exactly-512×512 PNG
/// (<see cref="ImageNormalizeService.NormalizeCatalogAssetAsync"/>'s own gate order) and receives a
/// <see langword="true"/> result here knows the returned bytes carry only <c>IHDR</c>/<c>PLTE</c>/
/// <c>IDAT</c>/<c>IEND</c>/<c>tRNS</c> chunks — the identical metadata-free shape the ffmpeg
/// <c>-map_metadata -1</c> re-encode path already guarantees, reached by chunk filtering instead of
/// pixel re-compression.
/// </para>
///
/// <para>
/// <b>FAIL-CLOSED, NOT FAIL-OPEN.</b> A truncated chunk, an out-of-range/overflow-shaped length, a
/// stream that never reaches <c>IEND</c>, or trailing bytes AFTER <c>IEND</c> — every one of these
/// returns <see langword="false"/> with <paramref name="output"/> left empty, never a best-effort
/// partial copy. The caller's own fallback (a full ffmpeg re-encode) is what decides such a stream's
/// fate — this class only ever takes the shortcut when it is fully confident the walk was clean.
/// </para>
/// </summary>
internal static class PngMetadataStripper
{
    /// <summary>The PNG signature's own fixed byte length — <see cref="PngImageHeader.HasSignature"/> is
    /// the one place that actually checks the signature's CONTENT (no re-declared copy of it here); this
    /// is only the offset the chunk walk below starts from once that check has already passed.</summary>
    const int SignatureLength = 8;

    /// <summary>PNG's own hard ceiling on a palette: at most 256 entries × 3 bytes/entry — a kept
    /// <c>PLTE</c> chunk's declared length is bounded to this AND must be a whole multiple of 3 (fix
    /// round finding #1): a naive length-prefixed copy has no opinion on whether a declared length is a
    /// real palette shape at all, so a non-conforming length is refused rather than copied through.
    /// </summary>
    const int MaxPlteChunkLength = 256 * 3;

    /// <summary>PNG's own ceiling on a <c>tRNS</c> chunk: at most one byte per palette entry (fix round
    /// finding #1) — 256, mirroring <see cref="MaxPlteChunkLength"/>'s own entry count (a truecolor/
    /// grayscale <c>tRNS</c> is even smaller — 6/2 bytes — so 256 is already generous headroom, never a
    /// length a genuine <c>tRNS</c> chunk needs to approach).</summary>
    const int MaxTrnsChunkLength = 256;

    /// <summary>
    /// Attempts the chunk-strip fast path over <paramref name="input"/> — see this type's own remarks
    /// for the full contract. On success, <paramref name="output"/> carries the signature plus every
    /// kept chunk, verbatim and in stream order (PNG's own chunk-ordering rules — <c>IHDR</c> first,
    /// <c>PLTE</c>/<c>tRNS</c> before the first <c>IDAT</c>, <c>IEND</c> last — are preserved for free,
    /// since this is a pure filter over the original order, never a reordering). On
    /// <see langword="false"/>, <paramref name="output"/> is an empty array and the caller must not
    /// treat this as "the input carries no metadata to strip" — it means "this walk could not
    /// confidently complete," which is a DIFFERENT claim entirely.
    /// </summary>
    public static bool TryStrip(ReadOnlySpan<byte> input, out byte[] output)
    {
        output = [];

        if (!PngImageHeader.HasSignature(input))
            return false;

        using var kept = new MemoryStream(input.Length);
        kept.Write(input[..SignatureLength]);

        var offset = SignatureLength;
        var sawIend = false;
        while (offset + 8 <= input.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset, 4));
            var type = input.Slice(offset + 4, 4);

            // chunk header(8) + data(length) + crc(4) — the SAME overflow/out-of-range discipline as
            // PngImageHeader.HasAnimationChunk: a malformed chunk ends the walk (returns false)
            // instead of wrapping into a negative/runaway advance.
            var chunkLength = 8L + length + 4;
            if (length > int.MaxValue - 12 || offset + chunkLength > input.Length)
                return false;

            // Fix round finding #1: a KEPT chunk type's own declared length gets a per-type bound
            // BEFORE it is ever copied — a naive length-prefixed copy trusts the declared length
            // unconditionally, which is exactly how a non-zero-length IEND smuggles an arbitrary
            // payload straight through a "metadata-free by construction" claim (the proven gh-#520
            // fix-round repro: a 130-byte payload riding inside a "IEND" chunk that this walk would
            // otherwise treat as a perfectly ordinary last chunk). A chunk type this walk does not
            // even keep is never written to kept regardless of its own length, so only the five
            // IsKeptChunkType members need an opinion here at all.
            var isKept = IsKeptChunkType(type);
            if (isKept && !IsWithinDeclaredLengthBound(type, length))
                return false;

            var chunk = input.Slice(offset, (int)chunkLength);
            if (isKept)
                kept.Write(chunk);

            offset += (int)chunkLength;

            if (type.SequenceEqual("IEND"u8))
            {
                sawIend = true;
                break;
            }
        }

        // A well-formed PNG's IEND is its own last chunk — a walk that never reached IEND, or one
        // that did but left bytes trailing after it, is not a shape this fast path trusts (fail-closed
        // rather than silently accepting a truncated or polyglot-tail stream).
        if (!sawIend || offset != input.Length)
            return false;

        output = kept.ToArray();
        return true;
    }

    static bool IsKeptChunkType(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("IHDR"u8) || type.SequenceEqual("PLTE"u8) || type.SequenceEqual("IDAT"u8) ||
        type.SequenceEqual("tRNS"u8) || type.SequenceEqual("IEND"u8);

    /// <summary>
    /// The per-type declared-length bound a KEPT chunk must satisfy before this walk ever copies it
    /// (fix round finding #1) — <c>IHDR</c>/<c>IDAT</c> carry no bound of their own here (a malformed
    /// <c>IHDR</c> already fails <see cref="PngImageHeader.TryReadDimensions"/> upstream of this class
    /// ever running, and an oversized <c>IDAT</c> is real pixel data, not a smuggling vector — the
    /// caller's own <see cref="ImageNormalizeService.MaxInputBytes"/> already bounds the whole file).
    /// <list type="bullet">
    /// <item><c>IEND</c> must be exactly zero-length — the PNG spec's own definition of that chunk; any
    /// other length is not "IEND with metadata," it is an arbitrary payload wearing IEND's name.</item>
    /// <item><c>tRNS</c> is bounded to <see cref="MaxTrnsChunkLength"/>.</item>
    /// <item><c>PLTE</c> is bounded to <see cref="MaxPlteChunkLength"/> AND must be a whole multiple of
    /// 3 (one RGB triplet per palette entry) — a length that is in-range but not a multiple of 3 is not
    /// a real palette either.</item>
    /// </list>
    /// </summary>
    static bool IsWithinDeclaredLengthBound(ReadOnlySpan<byte> type, uint length)
    {
        if (type.SequenceEqual("IEND"u8))
            return length == 0;
        if (type.SequenceEqual("tRNS"u8))
            return length <= MaxTrnsChunkLength;
        if (type.SequenceEqual("PLTE"u8))
            return length <= MaxPlteChunkLength && length % 3 == 0;

        return true;
    }
}
