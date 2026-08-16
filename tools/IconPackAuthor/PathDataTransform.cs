using System.Globalization;
using System.Text;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Scales one SVG <c>path</c> element's <c>d</c> attribute from its source viewBox down to the
/// house's fixed 16×16 icon frame (SPEC F130.1 has no per-pack viewBox/transform member — every icon
/// is authored AT 16×16, so a 24×24 (or other) source must be rescaled numerically at authoring time,
/// not deferred to a renderer). This is the one piece of real work PLAN T305 calls out explicitly:
/// path data is a tiny command grammar (a letter, then one or more numeric parameter groups — SVG
/// lets a command letter repeat implicitly by just supplying more numbers), and the arc command's
/// <c>large-arc-flag</c>/<c>sweep-flag</c> are NOT lengths — scaling them would corrupt the curve, not
/// merely resize it.
///
/// <para>
/// <b>Output is always fully explicit</b> — every repeated coordinate group re-emits its own command
/// letter, even where the source relied on SVG's implicit-repeat shorthand. This sidesteps needing to
/// reproduce that ambiguity in the output (harder to get wrong, trivially still within the parser's own
/// <c>IconPackDefinitionParser.PathDataText</c> character class) at the cost of a slightly longer
/// string — never a concern at icon-glyph scale.
/// </para>
///
/// <para>
/// <b>M's own quirk</b>: after the FIRST coordinate pair, further pairs following an <c>M</c>/<c>m</c>
/// with no new command letter are, per the SVG spec itself, treated as an implicit <c>L</c>/<c>l</c>
/// (moveto only ever "moves" once; repeats draw). This transform reproduces that by emitting
/// <c>L</c>/<c>l</c> for every group after the first — never re-emitting <c>M</c>/<c>m</c> for them.
/// </para>
/// </summary>
public static class PathDataTransform
{
    // x/y-coordinate arity per command letter (case-insensitive) — every param in every one of these
    // groups is a length, so every param scales uniformly. 'A' is handled separately (BuildArcGroup)
    // since its 7 params mix lengths, an unscaled rotation angle, and two unscaled flags. 'Z' takes no
    // params at all (closepath).
    static readonly IReadOnlyDictionary<char, int> CoordinateArity = new Dictionary<char, int>
    {
        ['M'] = 2, ['L'] = 2, ['T'] = 2,
        ['H'] = 1, ['V'] = 1,
        ['S'] = 4, ['Q'] = 4,
        ['C'] = 6,
    };

    /// <summary>Scales <paramref name="d"/>'s every length-bearing number by <paramref name="scale"/>.
    /// Throws <see cref="SvgConversionException"/> naming the malformed token the moment the grammar
    /// breaks — never silently drops a segment.</summary>
    public static string Scale(string d, double scale)
    {
        var scanner = new SvgNumberScanner(d);
        var output = new StringBuilder();
        char? currentCommand = null;
        var sawAnyCommand = false;

        while (!scanner.AtEnd)
        {
            var letter = scanner.TryReadCommandLetter();
            var isFirstGroupOfThisLetter = letter is not null;
            if (letter is { } newLetter)
            {
                currentCommand = newLetter;
                sawAnyCommand = true;
            }

            if (currentCommand is not { } command)
            {
                // Two distinct ways to land here with no live command letter: never having read one at
                // all (the string doesn't start with a command), or having just closed a 'Z' (arity
                // zero — currentCommand is reset to null below) with more, non-command-letter data
                // still following it. Same code path, two different faults — name the one that
                // actually happened rather than always blaming "does not start with."
                throw new SvgConversionException(sawAnyCommand
                    ? "path data has content after 'Z' (closepath) that is not a new command letter"
                    : "path data does not start with a command letter");
            }

            var upper = char.ToUpperInvariant(command);

            if (upper == 'Z')
            {
                output.Append(command);
                currentCommand = null; // Z takes no params; the NEXT token must be a fresh command letter.
                continue;
            }

            if (upper == 'A')
            {
                AppendArcGroup(output, scanner, command, scale);
                continue;
            }

            var arity = CoordinateArity[upper];
            var effectiveCommand = upper == 'M' && !isFirstGroupOfThisLetter
                ? (char.IsUpper(command) ? 'L' : 'l')
                : command;

            output.Append(effectiveCommand);
            for (var i = 0; i < arity; i++)
            {
                var raw = scanner.ReadNumber($"path command '{command}'");
                output.Append(' ').Append(FormatNumber(raw * scale));
            }
        }

        return output.ToString();
    }

    static void AppendArcGroup(StringBuilder output, SvgNumberScanner scanner, char command, double scale)
    {
        var rx = scanner.ReadNumber("arc command 'A' rx");
        var ry = scanner.ReadNumber("arc command 'A' ry");
        var xAxisRotation = scanner.ReadNumber("arc command 'A' x-axis-rotation"); // an ANGLE — never scaled
        var largeArcFlag = scanner.ReadFlag("arc command 'A' large-arc-flag");     // a FLAG — never scaled
        var sweepFlag = scanner.ReadFlag("arc command 'A' sweep-flag");            // a FLAG — never scaled
        var x = scanner.ReadNumber("arc command 'A' x");
        var y = scanner.ReadNumber("arc command 'A' y");

        output.Append(command)
            .Append(' ').Append(FormatNumber(rx * scale))
            .Append(' ').Append(FormatNumber(ry * scale))
            .Append(' ').Append(FormatNumber(xAxisRotation))
            .Append(' ').Append(largeArcFlag)
            .Append(' ').Append(sweepFlag)
            .Append(' ').Append(FormatNumber(x * scale))
            .Append(' ').Append(FormatNumber(y * scale));
    }

    /// <summary>Rounds <paramref name="value"/> to 4 decimal places — 0.0001 of this house's fixed
    /// 16×16 icon frame is sub-visual, so anything finer is noise, not signal — then formats the
    /// rounded value as a plain fixed-point decimal with trailing zeros trimmed (<c>"0.####"</c>).
    /// Rounding FIRST, before formatting, is deliberate: .NET's own shortest-round-trippable
    /// <see cref="double.ToString()"/> can still inflate an unrounded value like <c>0.1 + 0.2</c> into
    /// 17 significant digits, and can fall back to <c>E</c>/<c>e</c> scientific notation for a tiny
    /// magnitude — both outside <c>IconPackDefinitionParser.PointsText</c>'s character class, which
    /// (unlike <c>PathDataText</c>) excludes <c>E</c>/<c>e</c> entirely. Every character a rounded,
    /// fixed-point <c>"0.####"</c> string can ever produce — digits, an optional leading <c>-</c>, an
    /// optional <c>.</c>, never an exponent — sits inside BOTH grammars, so no further escaping is
    /// possible or needed. A rounded value of exactly zero (including negative zero, since IEEE-754
    /// treats <c>-0.0 == 0.0</c>) is normalized to the literal <c>"0"</c> rather than the surprising
    /// <c>"-0"</c> <c>"0.####"</c> would otherwise emit for a small negative input.</summary>
    internal static string FormatNumber(double value)
    {
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "0" : rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
