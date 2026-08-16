using System.Globalization;
using System.Xml.Linq;

namespace GenWave.IconPackAuthor;

/// <summary>
/// A source SVG's own coordinate frame, reduced to the one number every element's geometry scales
/// by. SPEC F130.1's <c>gw-icon-pack</c> schema carries no per-icon viewBox or transform — every icon
/// is authored AT the fixed 16×16 frame — so a 24×24 (or other) source must be rescaled numerically
/// here, at authoring time, once per glyph. Deliberately requires a SQUARE source frame with a 0,0
/// origin: MIT icon sets (heroicons/tabler/phosphor) all ship exactly this shape, and a non-square or
/// offset source would need a genuine 2-axis affine transform this simple ratio cannot express —
/// failing loudly here is preferable to silently distorting a glyph.
/// </summary>
/// <param name="Size">The source viewBox/width-height's own edge length (equal for both axes).</param>
/// <param name="Scale">The single multiplier every numeric geometry value in this glyph is scaled
/// by: <c>16 / Size</c>.</param>
public readonly record struct SvgFrame(double Size, double Scale)
{
    const double HouseFrameSize = 16.0;

    /// <summary>Reads <paramref name="root"/>'s <c>viewBox</c> (preferred) or <c>width</c>/<c>height</c>
    /// fallback. Throws <see cref="SvgConversionException"/> for anything that isn't a clean, square,
    /// zero-origin frame.</summary>
    public static SvgFrame Parse(XElement root)
    {
        var size = root.Attribute("viewBox")?.Value is { } viewBox
            ? ParseViewBox(viewBox)
            : ParseWidthHeightFallback(root);

        if (size <= 0)
            throw new SvgConversionException($"frame size {size} is not positive");

        return new SvgFrame(size, HouseFrameSize / size);
    }

    static double ParseViewBox(string viewBox)
    {
        var parts = viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !TryParseAll(parts, out var numbers))
            throw new SvgConversionException($"'viewBox=\"{viewBox}\"' is not four numbers");

        var (minX, minY, width, height) = (numbers[0], numbers[1], numbers[2], numbers[3]);
        if (minX != 0 || minY != 0)
            throw new SvgConversionException($"'viewBox' origin must be \"0 0 ...\", found \"{viewBox}\"");

        RequireSquare(width, height);
        return width;
    }

    static double ParseWidthHeightFallback(XElement root)
    {
        var widthAttr = root.Attribute("width")?.Value;
        var heightAttr = root.Attribute("height")?.Value;
        if (widthAttr is null || heightAttr is null ||
            !double.TryParse(widthAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(heightAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            throw new SvgConversionException("no 'viewBox' and no numeric 'width'/'height' fallback either");

        RequireSquare(width, height);
        return width;
    }

    static void RequireSquare(double width, double height)
    {
        if (Math.Abs(width - height) > 0.0001)
        {
            throw new SvgConversionException(
                $"frame is {width}x{height} — only a square source frame scales cleanly into the fixed {HouseFrameSize}x{HouseFrameSize} icon frame");
        }
    }

    static bool TryParseAll(string[] tokens, out double[] numbers)
    {
        numbers = new double[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        return true;
    }
}
