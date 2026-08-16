namespace GenWave.Host.Icons;

/// <summary>
/// One geometry primitive inside an icon's element list (SPEC F130.1, STORY-337, PLAN T302) — the
/// closed whitelist (<c>path|rect|circle|ellipse|line|polyline|polygon</c>), each carrying only
/// numeric geometry attributes plus an optional per-element <c>fill</c>/<c>stroke</c> override
/// restricted to the same <c>none|currentColor</c> vocabulary as <see cref="IconPackStyle"/>. Closed
/// hierarchy (private base constructor) — mirrors <c>GenWave.Core.Domain.PersonaImportOutcome</c>'s
/// own shape — so a future renderer (PLAN T304) switches over it exhaustively, no discard arm. Only
/// <see cref="IconPackDefinitionParser"/> constructs one, and only after every attribute has already
/// passed its own whitelist/grammar/finite-number gate — script, hrefs, CSS, and literal colors are
/// structurally unrepresentable by this hierarchy, not merely rejected at the edge.
/// </summary>
public abstract record IconElement
{
    private IconElement() { }

    /// <summary><c>&lt;path d="..."/&gt;</c> — <see cref="D"/> has already matched the path-data
    /// character grammar (SPEC F130.1: <c>[MmLlHhVvCcSsQqTtAaZz0-9 ,.+eE-]</c>); it can express no
    /// other SVG feature (no script, no href, no CSS) by construction.</summary>
    public sealed record Path(string D, string? Fill, string? Stroke) : IconElement;

    /// <summary><c>&lt;rect x y width height rx? ry?/&gt;</c>.</summary>
    public sealed record Rect(
        double X, double Y, double Width, double Height, double? Rx, double? Ry, string? Fill, string? Stroke)
        : IconElement;

    /// <summary><c>&lt;circle cx cy r/&gt;</c>.</summary>
    public sealed record Circle(double Cx, double Cy, double R, string? Fill, string? Stroke) : IconElement;

    /// <summary><c>&lt;ellipse cx cy rx ry/&gt;</c>.</summary>
    public sealed record Ellipse(double Cx, double Cy, double Rx, double Ry, string? Fill, string? Stroke) : IconElement;

    /// <summary><c>&lt;line x1 y1 x2 y2/&gt;</c>.</summary>
    public sealed record Line(double X1, double Y1, double X2, double Y2, string? Fill, string? Stroke) : IconElement;

    /// <summary><c>&lt;polyline points="..."/&gt;</c> — <see cref="Points"/> has already matched a
    /// numeric-list character grammar (digits, sign, decimal point, comma, space — no SVG command
    /// letters, unlike <see cref="Path.D"/>, since a bare coordinate list has no use for them).</summary>
    public sealed record Polyline(string Points, string? Fill, string? Stroke) : IconElement;

    /// <summary><c>&lt;polygon points="..."/&gt;</c>.</summary>
    public sealed record Polygon(string Points, string? Fill, string? Stroke) : IconElement;
}
