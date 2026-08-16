using System.Globalization;
using System.Xml.Linq;
using GenWave.Host.Icons;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Converts ONE already-whitelisted-by-tag SVG element (<see cref="SvgGlyphConverter"/> has already
/// checked the tag itself) into the matching <see cref="IconElement"/> case, scaling every numeric
/// geometry attribute by the glyph's own <see cref="SvgFrame.Scale"/> and enforcing the closed
/// per-tag attribute set SPEC F130.1 defines — mirrors <c>IconPackDefinitionParser</c>'s own
/// <c>ElementSchemas</c> table one layer upstream of it (that type validates already-whitelisted JSON;
/// this type is what PRODUCES whitelist-conforming data from arbitrary source markup in the first
/// place, so unlike the parser this one throws loudly — <see cref="SvgConversionException"/> — the
/// instant a source element carries something outside that set, rather than reporting "invalid" for a
/// caller to retry).
/// </summary>
public static class SvgPrimitiveConverter
{
    static readonly IReadOnlySet<string> FillStrokeOnly = new HashSet<string>(StringComparer.Ordinal) { "fill", "stroke" };

    public static IconElement Convert(XElement child, string context, double scale) => child.Name.LocalName switch
    {
        "path" => ConvertPath(child, context, scale),
        "rect" => ConvertRect(child, context, scale),
        "circle" => ConvertCircle(child, context, scale),
        "ellipse" => ConvertEllipse(child, context, scale),
        "line" => ConvertLine(child, context, scale),
        "polyline" => ConvertPolyline(child, context, scale),
        "polygon" => ConvertPolygon(child, context, scale),
        var tag => throw new SvgConversionException($"{context}: unhandled whitelisted tag '{tag}'"),
    };

    static IconElement ConvertPath(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["d", .. FillStrokeOnly], context);
        var d = RequiredAttribute(child, "d", context);
        return new IconElement.Path(PathDataTransform.Scale(d, scale), ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertRect(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["x", "y", "width", "height", "rx", "ry", .. FillStrokeOnly], context);
        var x = ReadRequiredNumber(child, "x", context) * scale;
        var y = ReadRequiredNumber(child, "y", context) * scale;
        var width = ReadRequiredNumber(child, "width", context) * scale;
        var height = ReadRequiredNumber(child, "height", context) * scale;
        var rx = ReadOptionalNumber(child, "rx", context) is { } rawRx ? rawRx * scale : (double?)null;
        var ry = ReadOptionalNumber(child, "ry", context) is { } rawRy ? rawRy * scale : (double?)null;
        return new IconElement.Rect(x, y, width, height, rx, ry, ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertCircle(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["cx", "cy", "r", .. FillStrokeOnly], context);
        var cx = ReadRequiredNumber(child, "cx", context) * scale;
        var cy = ReadRequiredNumber(child, "cy", context) * scale;
        var r = ReadRequiredNumber(child, "r", context) * scale;
        return new IconElement.Circle(cx, cy, r, ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertEllipse(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["cx", "cy", "rx", "ry", .. FillStrokeOnly], context);
        var cx = ReadRequiredNumber(child, "cx", context) * scale;
        var cy = ReadRequiredNumber(child, "cy", context) * scale;
        var rx = ReadRequiredNumber(child, "rx", context) * scale;
        var ry = ReadRequiredNumber(child, "ry", context) * scale;
        return new IconElement.Ellipse(cx, cy, rx, ry, ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertLine(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["x1", "y1", "x2", "y2", .. FillStrokeOnly], context);
        var x1 = ReadRequiredNumber(child, "x1", context) * scale;
        var y1 = ReadRequiredNumber(child, "y1", context) * scale;
        var x2 = ReadRequiredNumber(child, "x2", context) * scale;
        var y2 = ReadRequiredNumber(child, "y2", context) * scale;
        return new IconElement.Line(x1, y1, x2, y2, ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertPolyline(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["points", .. FillStrokeOnly], context);
        var points = RequiredAttribute(child, "points", context);
        return new IconElement.Polyline(PointsTransform.Scale(points, scale), ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static IconElement ConvertPolygon(XElement child, string context, double scale)
    {
        RequireNoUnknownAttributes(child, ["points", .. FillStrokeOnly], context);
        var points = RequiredAttribute(child, "points", context);
        return new IconElement.Polygon(PointsTransform.Scale(points, scale), ReadColorOverride(child, "fill", context), ReadColorOverride(child, "stroke", context));
    }

    static string RequiredAttribute(XElement child, string attr, string context) =>
        child.Attribute(attr)?.Value ?? throw new SvgConversionException($"{context} is missing required attribute '{attr}'");

    static double ReadRequiredNumber(XElement child, string attr, string context)
    {
        var raw = RequiredAttribute(child, attr, context);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new SvgConversionException($"{context} attribute '{attr}' = '{raw}' is not a finite number");

        return value;
    }

    static double? ReadOptionalNumber(XElement child, string attr, string context)
    {
        var raw = child.Attribute(attr)?.Value;
        if (raw is null)
            return null;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new SvgConversionException($"{context} attribute '{attr}' = '{raw}' is not a finite number");

        return value;
    }

    static string? ReadColorOverride(XElement child, string attr, string context)
    {
        var raw = child.Attribute(attr)?.Value;
        if (raw is null)
            return null;

        if (raw is "none" or "currentColor")
            return raw;

        throw new SvgConversionException(
            $"{context} has {attr}=\"{raw}\" — literal colors are not expressible by this schema; use \"none\" or \"currentColor\"");
    }

    static void RequireNoUnknownAttributes(XElement child, IReadOnlyCollection<string> allowed, string context)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var attribute in child.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            var name = attribute.Name.LocalName;
            if (allowedSet.Contains(name))
                continue;

            if (name == "style")
                throw new SvgConversionException($"{context} has a 'style' attribute — CSS is not expressible by this schema");

            throw new SvgConversionException(
                $"{context} has attribute '{name}' outside the closed attribute set for <{child.Name.LocalName}> ({string.Join(", ", allowedSet.OrderBy(a => a, StringComparer.Ordinal))})");
        }
    }
}
