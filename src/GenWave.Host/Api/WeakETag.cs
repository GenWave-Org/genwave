using System.Globalization;

namespace GenWave.Host.Api;

/// <summary>
/// The one shared implementation of GenWave's weak-<c>ETag</c> wire format (RFC 7232
/// <c>W/"&lt;token&gt;"</c>) — format, strip, and validate, all in one place. Both
/// <see cref="SponsorsController"/> and <see cref="AdsController"/> call this; each controller keeps
/// its own thin <c>ResolveIfMatch</c> wrapper that maps <see cref="TryParseVersion"/>'s pass/fail
/// outcome to ITS OWN 428/400 <c>ProblemDetails</c> wording — that mapping is controller-local phrasing,
/// not a copy of this seam (T434 round-2 review finding F3).
///
/// <para>
/// <c>MediaController.Patch</c> validates through <see cref="TryParseVersion"/> too (gh-#669): a
/// malformed <c>If-Match</c> is a 400, never a Postgres 22P02 from the <c>::xid</c> cast.
/// </para>
/// </summary>
internal static class WeakETag
{
    /// <summary>Formats a raw Postgres <c>xmin</c> token as a weak <c>ETag</c>:
    /// <c>W/"&lt;version&gt;"</c>.</summary>
    public static string Format(string version) => $"W/\"{version}\"";

    /// <summary>
    /// Strips the weak-<c>ETag</c> wrapper (<c>W/"&lt;token&gt;"</c>) or a plain quoted wrapper
    /// (<c>"&lt;token&gt;"</c>) from <paramref name="etag"/>, returning the token inside. Returns the
    /// trimmed input unchanged if neither wrapper is present — a caller that needs to REFUSE a
    /// malformed token should validate the result (see <see cref="TryParseVersion"/>), not trust this
    /// method alone to catch that case.
    /// </summary>
    public static string Strip(string etag)
    {
        var tag = etag.Trim();
        if (tag.StartsWith("W/\"", StringComparison.Ordinal) && tag.EndsWith('"'))
            return tag[3..^1];
        if (tag.StartsWith('"') && tag.EndsWith('"'))
            return tag[1..^1];
        return tag;
    }

    /// <summary>
    /// Strips <paramref name="raw"/>'s weak-<c>ETag</c> wrapper (see <see cref="Strip"/>) then parses
    /// the token as an unsigned 32-bit integer — Postgres's own <c>xid</c> domain — BEFORE it can ever
    /// reach a SQL <c>::xid</c> cast: returns <see langword="false"/> (and <paramref name="version"/> as
    /// <c>""</c>) for a <see langword="null"/>/blank/non-numeric token, never letting a raw
    /// <see cref="Npgsql.PostgresException"/> 22P02 reach a caller downstream.
    /// </summary>
    public static bool TryParseVersion(string? raw, out string version)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            version = "";
            return false;
        }

        var stripped = Strip(raw);
        if (!uint.TryParse(stripped, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            version = "";
            return false;
        }

        version = stripped;
        return true;
    }
}
