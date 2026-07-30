using System.Net;

namespace GenWave.MediaLibrary.Enrich;

/// <summary>
/// Normalizes a raw tag string at the ONE point external tag text enters the catalog (gh-#257):
/// blank collapses to null (the pre-existing honest-absence rule), and HTML character references
/// are decoded exactly once. Some export pipelines write entity-encoded tags — an artist of
/// literally <c>Paul &amp;amp; Manuel</c> — which then travel verbatim through catalog → annotate →
/// engine echo → now-playing/play-history and reach every display surface (admin and spectator
/// both render text nodes, never HTML, so the entity shows on screen). Decoding here — not per
/// display surface — keeps every downstream layer a pure pass-through: the annotate round trip
/// (<c>LiquidsoapAnnotationBuilder</c> → engine echo → <c>EngineMetadata.ExtractAnnotations</c>)
/// neither encodes nor decodes, so the decoded value survives end to end.
/// <para>
/// A plain ampersand is untouched (<see cref="WebUtility.HtmlDecode(string)"/> only rewrites
/// well-formed character references — <c>R&amp;B</c> stays <c>R&amp;B</c>), and double-encoded
/// input is deliberately decoded a single step (<c>&amp;amp;amp;</c> → <c>&amp;amp;</c>) —
/// decode-once, never decode-until-stable. Also verified while root-causing gh-#257: no icecast
/// status read-back exists on the now-playing path (the only icecast poll is
/// <c>IcecastListenerStatsSource</c>'s listener count, parsed with <c>XDocument</c> which decodes
/// XML entities itself), so this is the single injection point for encoded text.
/// </para>
/// </summary>
internal static class TagText
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var decoded = WebUtility.HtmlDecode(raw);
        // Re-check after the decode: an entity that decodes to pure whitespace (e.g. "&nbsp;")
        // must collapse to null exactly like a literal blank would have.
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }
}
