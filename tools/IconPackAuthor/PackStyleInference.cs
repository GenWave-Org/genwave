using GenWave.Host.Icons;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Reconciles every successfully converted glyph's own <see cref="GlyphStyleHint"/> into the ONE
/// pack-level <see cref="IconPackStyle"/> SPEC F130.1 allows — a real MIT set ships outline and solid
/// glyphs in separate folders/runs, so in practice every hint in a run agrees; this only needs a
/// deterministic tie-break when they don't (first hint wins, in the same filename-sorted order
/// <see cref="Program"/> processes glyphs in) and a sane default when NONE of them express an opinion
/// at all (a bare <c>&lt;path&gt;</c> with no root <c>fill</c>/<c>stroke-width</c>).
/// </summary>
public static class PackStyleInference
{
    // Heroicons' own outline defaults — reasonable house fallback for a source glyph that states no
    // opinion at all; 1.5 sits comfortably inside SPEC F130.1's [0.5, 3] bound even before scaling.
    const string DefaultFill = "currentColor";
    const double DefaultStrokeWidth = 1.5;

    /// <summary><paramref name="fillOverride"/>/<paramref name="strokeWidthOverride"/> — the curator's
    /// own explicit choice (a CLI flag) — always wins over inference from <paramref name="hints"/>.</summary>
    public static IconPackStyle Resolve(
        IReadOnlyList<GlyphStyleHint> hints, string? fillOverride, double? strokeWidthOverride)
    {
        var fill = fillOverride
            ?? hints.Select(hint => hint.Fill).FirstOrDefault(f => f is not null)
            ?? DefaultFill;

        var strokeWidth = strokeWidthOverride
            ?? hints.Select(hint => hint.StrokeWidth).FirstOrDefault(sw => sw is not null)
            ?? DefaultStrokeWidth;

        return new IconPackStyle(strokeWidth, fill);
    }
}
