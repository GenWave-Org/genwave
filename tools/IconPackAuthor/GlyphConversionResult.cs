using GenWave.Host.Icons;

namespace GenWave.IconPackAuthor;

/// <summary>
/// One source SVG's own conversion outcome (PLAN T305, STORY-338 AC1's "or fails naming the offending
/// glyph"). Closed hierarchy (private base constructor) — mirrors <c>IconPackValidationResult</c>'s
/// own shape one layer upstream of it — so <see cref="Program"/> switches over it exhaustively.
/// </summary>
public abstract record GlyphConversionResult
{
    private GlyphConversionResult() { }

    /// <summary>The glyph converted cleanly. <see cref="StyleHint"/> is this ONE glyph's own root-level
    /// fill/stroke-width reading — <see cref="PackStyleInference"/> reconciles it against every other
    /// glyph in the run into the ONE pack-level style block SPEC F130.1 allows.</summary>
    public sealed record Success(IReadOnlyList<IconElement> Elements, GlyphStyleHint StyleHint) : GlyphConversionResult;

    /// <summary>The glyph carries a construct this schema cannot express. <see cref="Reason"/> always
    /// names the specific offending element/attribute — never a bare "invalid SVG" (mirrors
    /// <c>IconPackValidationResult.Invalid</c>'s own "always names the specific rule" discipline).</summary>
    public sealed record Failure(string Reason) : GlyphConversionResult;
}
