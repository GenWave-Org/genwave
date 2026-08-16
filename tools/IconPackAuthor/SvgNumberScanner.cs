using System.Globalization;

namespace GenWave.IconPackAuthor;

/// <summary>
/// A cursor over one SVG numeric-attribute string (a <c>d</c> or <c>points</c> value) implementing
/// just enough of the SVG number grammar to tokenize it correctly: an optional sign, digits, an
/// optional decimal point, and an optional exponent — with NO separator required between two numbers
/// when the second one's own sign or decimal point already disambiguates where it starts (real MIT
/// icon sets lean on this constantly — <c>"14.5-4.5"</c> is two numbers, <c>14.5</c> and <c>-4.5</c>,
/// glued with no space). <see cref="PathDataTransform"/> layers command-letter/arc-flag handling on
/// top; <see cref="PointsTransform"/> uses this directly (a <c>points</c> value is nothing BUT a
/// number list). Mutable by design — a scanner IS a moving cursor, mirrored by <see cref="Position"/>
/// advancing on every read; nothing outside this class ever needs to rewind it.
/// </summary>
public sealed class SvgNumberScanner(string text)
{
    /// <summary>Current index into <paramref name="text"/> — 0 at construction, <see cref="text"/>'s
    /// own length once fully consumed.</summary>
    public int Position { get; private set; }

    /// <summary>True once every character has been consumed (after <see cref="SkipSeparators"/>
    /// removes any trailing whitespace/commas).</summary>
    public bool AtEnd
    {
        get
        {
            SkipSeparators();
            return Position >= text.Length;
        }
    }

    /// <summary>Advances past any run of whitespace and commas — SVG's own free-form separator
    /// vocabulary between numbers, command letters, and flags.</summary>
    public void SkipSeparators()
    {
        while (Position < text.Length && (char.IsWhiteSpace(text[Position]) || text[Position] == ','))
            Position++;
    }

    /// <summary>Reads one command letter (any of <c>MmLlHhVvCcSsQqTtAaZz</c>) if the next non-
    /// separator character is one, WITHOUT consuming it if it is not (a bare number at this position
    /// means "implicit repeat of the previous command," which the caller — <see cref="PathDataTransform"/>
    /// — decides, not this scanner).</summary>
    public char? TryReadCommandLetter()
    {
        SkipSeparators();
        if (Position >= text.Length)
            return null;

        var candidate = text[Position];
        if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(candidate) < 0)
            return null;

        Position++;
        return candidate;
    }

    /// <summary>Reads one SVG number: <c>[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?</c>. Throws
    /// <see cref="SvgConversionException"/> naming <paramref name="context"/> if the next token is not
    /// a well-formed number.</summary>
    public double ReadNumber(string context)
    {
        SkipSeparators();

        var start = Position;
        if (Position < text.Length && (text[Position] == '+' || text[Position] == '-'))
            Position++;

        var sawDigits = false;
        while (Position < text.Length && char.IsAsciiDigit(text[Position]))
        {
            Position++;
            sawDigits = true;
        }

        if (Position < text.Length && text[Position] == '.')
        {
            Position++;
            while (Position < text.Length && char.IsAsciiDigit(text[Position]))
            {
                Position++;
                sawDigits = true;
            }
        }

        if (!sawDigits)
        {
            Position = start;
            throw new SvgConversionException($"{context}: expected a number at position {start}, found '{RemainderPreview()}'");
        }

        if (Position < text.Length && (text[Position] == 'e' || text[Position] == 'E'))
        {
            var exponentStart = Position;
            Position++;
            if (Position < text.Length && (text[Position] == '+' || text[Position] == '-'))
                Position++;

            if (Position < text.Length && char.IsAsciiDigit(text[Position]))
            {
                while (Position < text.Length && char.IsAsciiDigit(text[Position]))
                    Position++;
            }
            else
            {
                // "1e" with nothing after it isn't a real exponent — back off and leave the 'e' unread
                // (unreachable from the character grammar SVG numbers actually use, but a scanner
                // should never advance past what it successfully parsed).
                Position = exponentStart;
            }
        }

        var token = text[start..Position];
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new SvgConversionException($"{context}: '{token}' is not a finite number");

        return value;
    }

    /// <summary>Reads exactly one arc flag character (<c>0</c> or <c>1</c>) — SVG's own single-digit
    /// grammar for the arc command's <c>large-arc-flag</c>/<c>sweep-flag</c> params, which may sit
    /// glued directly against the number that follows with no separator at all (the classic
    /// <c>"...0 011 5..."</c> ambiguity a general number scan cannot resolve on its own).</summary>
    public int ReadFlag(string context)
    {
        SkipSeparators();
        if (Position >= text.Length)
            throw new SvgConversionException($"{context}: expected an arc flag (0 or 1), found end of data");

        var candidate = text[Position];
        if (candidate != '0' && candidate != '1')
            throw new SvgConversionException($"{context}: expected an arc flag (0 or 1), found '{candidate}'");

        Position++;
        return candidate - '0';
    }

    string RemainderPreview()
    {
        const int maxChars = 12;
        var remaining = text[Position..];
        return remaining.Length <= maxChars ? remaining : remaining[..maxChars] + "…";
    }
}
