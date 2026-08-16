namespace GenWave.IconPackAuthor;

/// <summary>
/// One glyph's own root <c>&lt;svg&gt;</c>-level <c>fill</c>/<c>stroke</c>/<c>stroke-width</c>
/// reading — the outline-vs-solid signal <see cref="PackStyleInference"/> reconciles across every
/// successfully converted glyph in a run into SPEC F130.1's single pack-level style block. The root
/// <c>&lt;svg&gt;</c> tag itself is never validated against the primitive whitelist (only its
/// children are — see <see cref="SvgGlyphConverter"/>'s own remarks), so these three readings are
/// informational only; nothing here is echoed into the emitted pack directly.
/// </summary>
/// <param name="Fill">The root's own <c>fill</c> attribute, if present and one of the two tokens this
/// schema can express (<c>none</c>/<c>currentColor</c>) — <see langword="null"/> otherwise.</param>
/// <param name="StrokeWidth">The root's own <c>stroke-width</c> attribute, parsed, if present.</param>
public sealed record GlyphStyleHint(string? Fill, double? StrokeWidth);
