using System.Text;

namespace GenWave.Host.Api;

/// <summary>
/// Shared bounded-body read for every route that must never trust <c>Content-Length</c> alone against
/// an untrusted body (SPEC F79.6, F90.7, F103.6, F128.6; <see cref="PersonaController.Import"/>,
/// <see cref="ThemesImportController.Import"/>, <see cref="PersonaAvatarController.Put"/>) — a security
/// control (size-cap enforcement) that must not exist as independently maintained copies (PLAN T184
/// review F4: it did, briefly, and this type is the fix).
///
/// <para>
/// <see cref="ReadBoundedBytesAsync"/> reads <c>Request.Body</c> up to a caller-supplied byte cap,
/// never trusting a client-declared <c>Content-Length</c> alone — a chunked request carries no
/// <c>Content-Length</c> header at all, so a header-only check would let one through unbounded. The
/// declared-length check is a fast reject when the client is honest about it; the running-total check
/// while reading is what actually enforces the cap either way, returning <c>Oversized: true</c> the
/// instant the total crosses the cap without buffering anything past that point.
/// <see cref="ReadBoundedBodyAsync"/> is the original JSON-shaped caller's own thin wrapper —
/// PLAN T295's raw-bytes upload (<see cref="PersonaAvatarController.Put"/>) needs the untranscoded
/// bytes themselves, never a UTF-8 string reinterpretation of binary image data, so it calls
/// <see cref="ReadBoundedBytesAsync"/> directly instead.
/// </para>
///
/// <para>
/// <see cref="Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute"/> is ALSO applied to every caller's
/// own action — real defense in depth for a Kestrel deployment, where exceeding it can short-circuit
/// even earlier — but it is
/// <see cref="Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature"/>-based and
/// <c>TestServer</c> (every route's own test suite) does not enforce that feature the way Kestrel's
/// transport does; this type's own read loop is what actually makes the cap real and testable
/// regardless of host.
/// </para>
/// </summary>
internal static class BoundedImportBodyReader
{
    /// <summary>The size cap both portable-import routes share (SPEC F79.6/F103.6) — a persona card
    /// and a theme manifest are the same order of magnitude either way: a handful of fields and, at
    /// most, a few font-face declarations or taste rules, never large.</summary>
    public const int MaxImportBytes = 256 * 1024;

    /// <summary>The <c>catalogSlug</c> length cap both routes share (SPEC F90.7) — a real catalog
    /// entry slug is a short, human-authored identifier, never anywhere near this long; checked BEFORE
    /// a caller's own slug-format regex so a pathological input never reaches the regex engine.</summary>
    public const int MaxCatalogSlugLength = 64;

    /// <summary>
    /// Reads <paramref name="request"/>'s body up to <paramref name="maxBytes"/> bytes, decoded as
    /// UTF-8 — the portable-JSON import routes' own shape. See this type's own remarks for the full
    /// "never trust Content-Length alone" reasoning.
    /// </summary>
    public static async Task<(string Json, bool Oversized)> ReadBoundedBodyAsync(
        HttpRequest request, int maxBytes, CancellationToken ct)
    {
        var (bytes, oversized) = await ReadBoundedBytesAsync(request, maxBytes, ct);
        return oversized ? (string.Empty, true) : (Encoding.UTF8.GetString(bytes), false);
    }

    /// <summary>
    /// Reads <paramref name="request"/>'s body up to <paramref name="maxBytes"/> bytes, as raw bytes —
    /// PLAN T295's own shape: a PNG/JPEG upload is never valid UTF-8 text, so
    /// <see cref="ReadBoundedBodyAsync"/>'s string decode would corrupt it. See this type's own remarks
    /// for the full "never trust Content-Length alone" reasoning, shared verbatim by both methods.
    /// </summary>
    public static async Task<(byte[] Bytes, bool Oversized)> ReadBoundedBytesAsync(
        HttpRequest request, int maxBytes, CancellationToken ct)
    {
        if (request.ContentLength is long declared && declared > maxBytes)
            return ([], true);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return ([], true);

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }
}
