namespace GenWave.IconPackAuthor;

/// <summary>
/// Scales one <c>polyline</c>/<c>polygon</c> element's <c>points</c> attribute — a flat, command-free
/// list of <c>x,y</c> coordinate pairs (SPEC F130.1's own <c>points</c> grammar, the parser's own
/// <c>IconPackDefinitionParser.PointsText</c>). Simpler than <see cref="PathDataTransform"/>: there is
/// no command letter, no implicit-repeat rule, and no arc flags — every number present is a
/// coordinate, so every number scales uniformly.
/// </summary>
public static class PointsTransform
{
    /// <summary>Scales every number in <paramref name="points"/> by <paramref name="scale"/>. Throws
    /// <see cref="SvgConversionException"/> the moment a token fails to parse as a number.</summary>
    public static string Scale(string points, double scale)
    {
        var scanner = new SvgNumberScanner(points);
        var scaled = new List<string>();

        while (!scanner.AtEnd)
        {
            var value = scanner.ReadNumber("'points' coordinate");
            scaled.Add(PathDataTransform.FormatNumber(value * scale));
        }

        if (scaled.Count == 0)
            throw new SvgConversionException("'points' has no coordinates");

        return string.Join(' ', scaled);
    }
}
