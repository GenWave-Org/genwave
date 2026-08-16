using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using GenWave.Host.Icons;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Converts ONE source SVG file into a whitelist-conforming <see cref="IconElement"/> list (PLAN
/// T305, STORY-338 AC1) — the real work this offline authoring script exists to do. Only a flat set of
/// primitive children directly under the root <c>&lt;svg&gt;</c> is supported: no <c>&lt;g&gt;</c>
/// wrapper, no <c>&lt;defs&gt;</c>, no nesting at all (SPEC F130.1's element list has no concept of a
/// group — a source icon relying on one is a structural mismatch, reported here rather than silently
/// flattened, which could reorder or duplicate geometry the source author never intended).
/// <see cref="SvgFrame"/> handles the viewBox→16×16 scale; <see cref="SvgPrimitiveConverter"/> handles
/// one element's own tag/attribute whitelist and numeric scaling.
/// </summary>
public static class SvgGlyphConverter
{
    static readonly IReadOnlySet<string> WhitelistedTags =
        new HashSet<string>(StringComparer.Ordinal) { "path", "rect", "circle", "ellipse", "line", "polyline", "polygon" };

    /// <summary>Converts <paramref name="sourcePath"/>. Never throws — every failure mode this method
    /// catches (malformed XML, a disallowed element/attribute, an inexpressible color, a non-square
    /// frame, a source file that cannot be read) becomes a <see cref="GlyphConversionResult.Failure"/>
    /// naming the source file and the offending construct.</summary>
    public static GlyphConversionResult Convert(string sourcePath)
    {
        try
        {
            return ConvertCore(sourcePath);
        }
        catch (SvgConversionException ex)
        {
            return new GlyphConversionResult.Failure($"{Path.GetFileName(sourcePath)}: {ex.Message}");
        }
        catch (XmlException ex)
        {
            return new GlyphConversionResult.Failure($"{Path.GetFileName(sourcePath)}: malformed XML ({ex.Message})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // PackAuthoringPipeline checks File.Exists immediately before calling here, but that is a
            // check-then-act race, not a guarantee — a file that vanishes, gets locked, or loses read
            // permission between the two must still surface as one more named glyph failure, not an
            // unhandled exception that aborts the whole batch mid-run.
            return new GlyphConversionResult.Failure($"{Path.GetFileName(sourcePath)}: could not read source file ({ex.Message})");
        }
    }

    static GlyphConversionResult ConvertCore(string sourcePath)
    {
        var document = XDocument.Load(sourcePath, LoadOptions.None);
        var root = document.Root;
        if (root is null)
            throw new SvgConversionException("empty document");
        if (root.Name.LocalName != "svg")
            throw new SvgConversionException($"root element is <{root.Name.LocalName}>, not <svg>");

        var frame = SvgFrame.Parse(root);
        var styleHint = ReadStyleHint(root, frame.Scale);

        var elements = new List<IconElement>();
        var index = 0;
        foreach (var child in root.Elements())
        {
            index++;
            var tag = child.Name.LocalName;
            var context = $"{Path.GetFileName(sourcePath)} element #{index} <{tag}>";

            if (!WhitelistedTags.Contains(tag))
            {
                throw new SvgConversionException(
                    $"{context} is outside the whitelist (only path|rect|circle|ellipse|line|polyline|polygon; " +
                    "wrapper/grouping elements like <g>/<defs> are not supported)");
            }

            elements.Add(SvgPrimitiveConverter.Convert(child, context, frame.Scale));
        }

        if (elements.Count == 0)
            throw new SvgConversionException($"{Path.GetFileName(sourcePath)} declares no primitive elements");

        return new GlyphConversionResult.Success(elements, styleHint);
    }

    /// <summary>Reads the root <c>&lt;svg&gt;</c>'s own <c>fill</c>/<c>stroke-width</c> as an
    /// outline-vs-solid hint — see <see cref="GlyphStyleHint"/>'s own remarks. <paramref name="scale"/>
    /// is applied to <c>stroke-width</c> immediately (a visual weight, scaled the same as every other
    /// length in this glyph) so <see cref="PackStyleInference"/> never needs to re-derive or re-carry
    /// each glyph's own scale factor. Never fails: an absent or unrecognized root-level hint just means
    /// "no opinion," resolved once every glyph in the run is in.</summary>
    static GlyphStyleHint ReadStyleHint(XElement root, double scale)
    {
        var fillAttr = root.Attribute("fill")?.Value;
        var fill = fillAttr is "none" or "currentColor" ? fillAttr : null;

        double? strokeWidth = root.Attribute("stroke-width")?.Value is { } raw &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed * scale
                : null;

        return new GlyphStyleHint(fill, strokeWidth);
    }
}
