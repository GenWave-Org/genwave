using System.Text;

namespace GenWave.Host.Api;

/// <summary>
/// Shared bounded-body read for the portable-JSON import routes (SPEC F79.6, F90.7, F103.6;
/// <see cref="PersonaController.Import"/>, <see cref="ThemesImportController.Import"/>) — a security
/// control (size-cap enforcement against an untrusted upload) that must not exist as two independently
/// maintained copies (PLAN T184 review F4: it did, briefly, and this type is the fix).
///
/// <para>
/// Reads <c>Request.Body</c> up to <see cref="MaxImportBytes"/> bytes, never trusting a
/// client-declared <c>Content-Length</c> alone — a chunked request carries no <c>Content-Length</c>
/// header at all, so a header-only check would let one through unbounded. The declared-length check is
/// a fast reject when the client is honest about it; the running-total check while reading is what
/// actually enforces the cap either way, returning <c>Oversized: true</c> the instant the total crosses
/// the cap without buffering anything past that point.
/// </para>
///
/// <para>
/// <see cref="Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute"/> is ALSO applied to both callers'
/// own actions — real defense in depth for a Kestrel deployment, where exceeding it can short-circuit
/// even earlier — but it is
/// <see cref="Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature"/>-based and
/// <c>TestServer</c> (both routes' own test suites) does not enforce that feature the way Kestrel's
/// transport does; <see cref="ReadBoundedBodyAsync"/> is what actually makes the cap real and testable
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
    /// Reads <paramref name="request"/>'s body up to <paramref name="maxBytes"/> bytes — see this
    /// type's own remarks for the full "never trust Content-Length alone" reasoning.
    /// </summary>
    public static async Task<(string Json, bool Oversized)> ReadBoundedBodyAsync(
        HttpRequest request, int maxBytes, CancellationToken ct)
    {
        if (request.ContentLength is long declared && declared > maxBytes)
            return (string.Empty, true);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return (string.Empty, true);

            buffer.Write(chunk, 0, read);
        }

        return (Encoding.UTF8.GetString(buffer.ToArray()), false);
    }
}
