namespace GenWave.Host.Icons;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Parses and validates ONE icon pack definition document (SPEC F130.1/F130.2, STORY-337, PLAN T302)
/// — the <c>gw-icon-pack</c>, schema-major 1 format. This is REMOTE, catalog-origin JSON (like
/// <c>Catalog.CatalogIndexValidator</c>/<c>Catalog.CatalogFontManifestSerializer</c>), never a
/// build-time embedded document (unlike <see cref="Theming.ThemeManifestParser"/>'s shipped themes),
/// so every failure is a <see cref="IconPackValidationResult.Invalid"/> return, never a thrown
/// exception — a hostile or malformed pack must never cost a caller more than "install refused",
/// mirroring <c>CatalogIndexValidator.TryValidate</c>'s own non-throwing posture.
///
/// <para>
/// <b>SCHEMA-MAJOR</b> — calls the shared <see cref="SchemaVersionProbe"/> (PLAN T302 review F4;
/// <see cref="GenWave.Host.Api.ThemeSchemaVersionGate"/> and <see cref="Shows.ShowManifestParser"/> now
/// call that same shared extraction too, rather than each holding its own verbatim copy) for the exact
/// three-outcome contract: an OPTIONAL top-level <c>schemaVersion</c> integer, read via a throwaway
/// <see cref="JsonDocument"/> parse BEFORE the real structural deserialize below ever runs (a newer
/// major is free to look nothing like today's v1 shape, so it must be caught before a structural
/// mismatch reports a confusing generic error instead). ABSENT ⇒ treated as
/// <see cref="CurrentSchemaVersion"/> and passes; PRESENT and over <see cref="CurrentSchemaVersion"/>
/// ⇒ refused, naming both; PRESENT but unreadable (a string, a fraction, an overflow) ⇒ ALSO refused,
/// never silently coerced to "absent". Unlike Theme/Show, this parser owns both steps itself — there
/// is no separate write-route controller yet (PLAN T303) to split the extraction into, and this class
/// stays "Host, pure" per its own task (no HTTP dependency to lean the split on).
/// </para>
///
/// <para>
/// <b>BOUNDS</b> (SPEC F130.1's ≤256 KiB cap, plus caps this parser adds since the SPEC leaves them
/// unstated): <see cref="MaxDefinitionBytes"/> is checked against the RAW byte length BEFORE any parse
/// attempt, exactly like <c>CatalogIndexValidator</c>'s own per-kind byte ceilings.
/// <see cref="MaxIconsPerPack"/> (512) and <see cref="MaxElementsPerIcon"/> (64) exist purely to bound
/// the O(icons × elements) whitelist walk below against a pack authored to be slow rather than unsafe
/// — 512 icons is already ~20× the house's own 26-name contract (<see cref="IconNameContract"/>), and
/// 64 elements is generously above every shipped house icon's own 2–6-element shape (see
/// <c>admin-ui/app/(authed)/_components/icons.tsx</c>); a pack legitimately needing more than either is
/// not a shape this schema is aimed at. <see cref="MaxIconNameChars"/> (64) is a DIFFERENT kind of
/// bound — not a slow-walk ceiling but a map-KEY character-shape gate (PLAN T302 review F1): every
/// <c>icons</c> map key must match <see cref="IconNameText"/> before its elements are ever inspected,
/// the same "gate map KEYS, not just map values" rule <c>Theming.ThemeManifestParser</c>'s own
/// <c>TokenNamePattern</c> established for theme token names (that type's own comment records the T159
/// round-2 ruling: caught HERE, at load) — applied here because an icon name is otherwise unconstrained
/// free text able to carry a hostile value (script tags, CRLF, a multi-KB string) all the way to
/// <see cref="IconPackDefinition.Icons"/>/<see cref="IconPackValidationResult.Valid.IgnoredNames"/>.
/// </para>
///
/// <para>
/// <b>WHOLE-DOCUMENT REJECT, NOT PER-ICON WITHHOLD.</b> Unlike <c>CatalogIndexValidator</c>'s own
/// per-entry withhold-and-continue ladder (a LISTING tolerating one bad entry), SPEC F130.5's install
/// route validates "schema + whitelist" as one gate: any element anywhere in the document failing its
/// own rule rejects the WHOLE definition — nothing partially installs. The one exception is names
/// outside <see cref="IconNameContract.Names"/> (SPEC F130.2): those are whitelist-VALID, ordinary
/// data, just never rendered under a name today's UI has a slot for, so a <see cref="IconPackValidationResult.Valid"/>
/// still reports them via <see cref="IconPackValidationResult.Valid.IgnoredNames"/> rather than
/// rejecting or silently dropping them.
/// </para>
/// </summary>
internal static partial class IconPackDefinitionParser
{
    /// <summary>The one schema major this parser currently accepts (mirrors
    /// <c>GenWave.Host.Api.ThemeSchemaVersionGate.CurrentSchemaVersion</c>/<c>Shows.ShowManifestParser.CurrentSchemaVersion</c>
    /// — each format versions independently, so this is icon packs' own copy, not a shared
    /// constant).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>SPEC F130.1's own cap, checked against the raw byte length before any parse.</summary>
    public const int MaxDefinitionBytes = 256 * 1024;

    /// <summary>See this type's own "BOUNDS" remarks.</summary>
    public const int MaxIconsPerPack = 512;

    /// <summary>See this type's own "BOUNDS" remarks.</summary>
    public const int MaxElementsPerIcon = 64;

    /// <summary>See this type's own "BOUNDS" remarks — the map-KEY character-shape gate (PLAN T302
    /// review F1), not a slow-walk bound.</summary>
    public const int MaxIconNameChars = 64;

    const double MinStrokeWidth = 0.5;
    const double MaxStrokeWidth = 3.0;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // SPEC F130.1's own path-data grammar, verbatim: letters MmLlHhVvCcSsQqTtAaZz (every SVG path
    // command), digits, space, comma, period, sign, and the exponent letters e/E. The literal '-'
    // is placed LAST in the class deliberately — inside "...+-eE]" (SPEC's own written order) a bare
    // '-' between '+' and 'e' would form a RANGE ('+' through 'e', U+002B..U+0065) rather than a
    // literal hyphen, silently admitting far more characters than the grammar intends (digits,
    // letters, and punctuation the surrounding `d="..."` SVG attribute must never see). Moving it to
    // the end of the class keeps it a literal without changing the character SET the SPEC describes.
    internal const string PathDataText = @"\A[MmLlHhVvCcSsQqTtAaZz0-9 ,.+eE-]+\z";

    [GeneratedRegex(PathDataText)]
    private static partial Regex PathDataPattern();

    // polyline/polygon's `points` is the same "numeric geometry, expressed as a string" shape `d`
    // is, minus the SVG command letters a bare coordinate list has no use for — the SPEC only names
    // `d`'s own grammar explicitly, but `points` carries the identical injection surface (it also
    // lands in a rendered SVG attribute) and SPEC F130.1 requires "numeric geometry attributes only"
    // of every primitive, so this parser holds it to the same discipline rather than leaving it an
    // unbounded string.
    internal const string PointsText = @"\A[0-9 ,.+-]+\z";

    [GeneratedRegex(PointsText)]
    private static partial Regex PointsPattern();

    // Every icon MAP KEY (SPEC F130.1's `icons` object) — mirrors
    // Theming.ThemeManifestParser.TokenNamePattern exactly (PLAN T302 review F1: that type's own T159
    // round-2 ruling was "a map KEY needs the identical gate a map VALUE gets, caught HERE at load", and
    // this parser's own icon names were left unconstrained free text until this review). Lowercase
    // letters, digits and hyphens, starting with a letter — rejects empty, CRLF, and
    // '</svg><script>…'-shaped names alike; length is capped separately by MaxIconNameChars.
    internal const string IconNameText = @"\A[a-z][a-z0-9-]*\z";

    [GeneratedRegex(IconNameText)]
    private static partial Regex IconNamePattern();

    static readonly IReadOnlyDictionary<string, ElementSchema> ElementSchemas = new Dictionary<string, ElementSchema>(StringComparer.Ordinal)
    {
        ["path"] = BuildSchema([], [], ("d", PathDataPattern()),
            (_, grammarValue, fill, stroke) => new IconElement.Path(grammarValue, fill, stroke)),
        ["rect"] = BuildSchema(["x", "y", "width", "height"], ["rx", "ry"], null,
            (numbers, _, fill, stroke) => new IconElement.Rect(
                numbers["x"], numbers["y"], numbers["width"], numbers["height"],
                numbers.TryGetValue("rx", out var rx) ? rx : null,
                numbers.TryGetValue("ry", out var ry) ? ry : null,
                fill, stroke)),
        ["circle"] = BuildSchema(["cx", "cy", "r"], [], null,
            (numbers, _, fill, stroke) => new IconElement.Circle(numbers["cx"], numbers["cy"], numbers["r"], fill, stroke)),
        ["ellipse"] = BuildSchema(["cx", "cy", "rx", "ry"], [], null,
            (numbers, _, fill, stroke) => new IconElement.Ellipse(numbers["cx"], numbers["cy"], numbers["rx"], numbers["ry"], fill, stroke)),
        ["line"] = BuildSchema(["x1", "y1", "x2", "y2"], [], null,
            (numbers, _, fill, stroke) => new IconElement.Line(numbers["x1"], numbers["y1"], numbers["x2"], numbers["y2"], fill, stroke)),
        ["polyline"] = BuildSchema([], [], ("points", PointsPattern()),
            (_, grammarValue, fill, stroke) => new IconElement.Polyline(grammarValue, fill, stroke)),
        ["polygon"] = BuildSchema([], [], ("points", PointsPattern()),
            (_, grammarValue, fill, stroke) => new IconElement.Polygon(grammarValue, fill, stroke)),
    };

    const string AllowedTagsText = "path, rect, circle, ellipse, line, polyline, polygon";

    /// <summary>
    /// Validates <paramref name="json"/> against every SPEC F130.1/F130.2 rule. Never throws for a
    /// malformed or hostile document — see this type's own remarks.
    /// </summary>
    public static IconPackValidationResult Validate(byte[] json)
    {
        if (json.Length > MaxDefinitionBytes)
            return new IconPackValidationResult.Invalid(
                $"icon pack definition is {json.Length} bytes, over the {MaxDefinitionBytes}-byte ({MaxDefinitionBytes / 1024} KiB) cap");

        (int? Version, bool Unreadable) schemaProbe;
        try
        {
            using var probeDocument = JsonDocument.Parse(json);
            schemaProbe = SchemaVersionProbe.Extract(probeDocument.RootElement);
        }
        catch (JsonException ex)
        {
            return new IconPackValidationResult.Invalid($"icon pack definition is malformed JSON ({ex.Message})");
        }

        if (schemaProbe.Unreadable)
            return new IconPackValidationResult.Invalid("icon pack definition's 'schemaVersion', when present, must be a whole number");

        if (schemaProbe.Version is { } version && version > CurrentSchemaVersion)
            return new IconPackValidationResult.Invalid(
                $"icon pack definition schema major {version} is newer than the {CurrentSchemaVersion} this app supports");

        IconPackDocumentJson? document;
        try
        {
            document = JsonSerializer.Deserialize<IconPackDocumentJson>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new IconPackValidationResult.Invalid($"icon pack definition is malformed JSON ({ex.Message})");
        }

        if (document is null)
            return new IconPackValidationResult.Invalid("icon pack definition is empty");

        if (!TryValidateStyle(document.Style, out var style, out var styleReason))
            return new IconPackValidationResult.Invalid(styleReason);

        if (document.Icons is not { Count: > 0 } rawIcons)
            return new IconPackValidationResult.Invalid("icon pack definition declares no icons");

        if (rawIcons.Count > MaxIconsPerPack)
            return new IconPackValidationResult.Invalid(
                $"icon pack definition declares {rawIcons.Count} icons, over the {MaxIconsPerPack}-icon-per-pack cap");

        var icons = new Dictionary<string, IReadOnlyList<IconElement>>(StringComparer.Ordinal);
        foreach (var (name, rawElements) in rawIcons)
        {
            if (!TryValidateIcon(name, rawElements, out var elements, out var iconReason))
                return new IconPackValidationResult.Invalid(iconReason);

            icons[name] = elements;
        }

        var ignoredNames = icons.Keys
            .Where(name => !IconNameContract.Names.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new IconPackValidationResult.Valid(new IconPackDefinition(style, icons), ignoredNames);
    }

    static bool TryValidateStyle(
        IconPackStyleJson? raw,
        [NotNullWhen(true)] out IconPackStyle? style,
        [NotNullWhen(false)] out string? reason)
    {
        style = null;
        reason = null;

        if (raw is null)
        {
            reason = "icon pack definition is missing its 'style' block";
            return false;
        }

        if (raw.StrokeWidth is not { } strokeWidth || !double.IsFinite(strokeWidth))
        {
            reason = "icon pack definition style is missing a finite numeric 'strokeWidth'";
            return false;
        }

        if (strokeWidth < MinStrokeWidth || strokeWidth > MaxStrokeWidth)
        {
            reason = $"icon pack definition style 'strokeWidth' {strokeWidth} is outside the [{MinStrokeWidth}, {MaxStrokeWidth}] range";
            return false;
        }

        if (raw.Fill is not { } fill || !IsColorToken(fill))
        {
            reason = $"icon pack definition style 'fill' must be 'none' or 'currentColor', not '{raw.Fill ?? "(missing)"}'";
            return false;
        }

        style = new IconPackStyle(strokeWidth, fill);
        return true;
    }

    static bool TryValidateIcon(
        string name,
        List<JsonElement>? rawElements,
        [NotNullWhen(true)] out IReadOnlyList<IconElement>? elements,
        [NotNullWhen(false)] out string? reason)
    {
        elements = null;
        reason = null;

        // Map-KEY gate (PLAN T302 review F1) — length is checked before shape, so an oversized name is
        // never itself echoed back into a message at full length.
        if (name.Length > MaxIconNameChars)
        {
            reason = $"icon pack definition has an icon name {name.Length} characters long, over the {MaxIconNameChars}-character cap";
            return false;
        }

        if (!IconNamePattern().IsMatch(name))
        {
            reason = $"icon pack definition has an icon name '{name}' outside the safe {IconNameText} shape";
            return false;
        }

        if (rawElements is not { Count: > 0 })
        {
            reason = $"icon '{name}' declares no elements";
            return false;
        }

        if (rawElements.Count > MaxElementsPerIcon)
        {
            reason = $"icon '{name}' has {rawElements.Count} elements, over the {MaxElementsPerIcon}-element-per-icon cap";
            return false;
        }

        var parsed = new List<IconElement>(rawElements.Count);
        for (var index = 0; index < rawElements.Count; index++)
        {
            if (!TryParseElement(new ElementContext(name, index), rawElements[index], out var element, out reason))
                return false;

            parsed.Add(element);
        }

        elements = parsed;
        return true;
    }

    static bool TryParseElement(
        ElementContext context,
        JsonElement raw,
        [NotNullWhen(true)] out IconElement? element,
        [NotNullWhen(false)] out string? reason)
    {
        element = null;
        reason = null;

        if (raw.ValueKind != JsonValueKind.Object)
        {
            reason = $"icon '{context.IconName}' element #{context.Index} must be a JSON object";
            return false;
        }

        if (!raw.TryGetProperty("tag", out var tagProperty) ||
            tagProperty.ValueKind != JsonValueKind.String ||
            tagProperty.GetString() is not { Length: > 0 } tag)
        {
            reason = $"icon '{context.IconName}' element #{context.Index} is missing a string 'tag'";
            return false;
        }

        if (!ElementSchemas.TryGetValue(tag, out var schema))
        {
            reason = $"icon '{context.IconName}' element #{context.Index} has tag '{tag}' outside the closed primitive whitelist ({AllowedTagsText})";
            return false;
        }

        var tagged = context with { Tag = tag };

        foreach (var property in raw.EnumerateObject())
        {
            if (!schema.AllowedAttributeNames.Contains(property.Name))
            {
                reason = $"icon '{tagged.IconName}' element #{tagged.Index} (tag '{tag}') has attribute '{property.Name}' outside the closed '{tag}' attribute set";
                return false;
            }
        }

        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var attr in schema.RequiredNumericAttrs)
        {
            if (!TryReadRequiredNumber(tagged, raw, attr, out var value, out reason))
                return false;

            numbers[attr] = value;
        }

        foreach (var attr in schema.OptionalNumericAttrs)
        {
            if (!TryReadOptionalNumber(tagged, raw, attr, out var value, out reason))
                return false;

            if (value is { } present)
                numbers[attr] = present;
        }

        // "" for every tag whose own schema.Grammar is null (rect/circle/ellipse/line's own factory
        // never reads it) — never null, so no factory below needs a null-forgiving read to use it.
        var grammarValue = "";
        if (schema.Grammar is { } grammar)
        {
            if (!raw.TryGetProperty(grammar.Attr, out var grammarProperty) || grammarProperty.ValueKind != JsonValueKind.String)
            {
                reason = $"icon '{tagged.IconName}' element #{tagged.Index} (tag '{tag}') is missing a string '{grammar.Attr}'";
                return false;
            }

            var text = grammarProperty.GetString() ?? "";
            if (!grammar.Pattern.IsMatch(text))
            {
                reason = $"icon '{tagged.IconName}' element #{tagged.Index} (tag '{tag}') attribute '{grammar.Attr}' does not match the required character grammar";
                return false;
            }

            grammarValue = text;
        }

        if (!TryValidateColorAttr(tagged, raw, "fill", out var fill, out reason))
            return false;

        if (!TryValidateColorAttr(tagged, raw, "stroke", out var stroke, out reason))
            return false;

        // ElementSchemas is the single source enumerating the seven primitive tags (PLAN T302 review
        // F3) — schema.Factory was paired with this exact tag when ElementSchemas was built, replacing
        // what was previously a second, independently-enumerated tag switch guarded only by a runtime
        // UnreachableException.
        element = schema.Factory(numbers, grammarValue, fill, stroke);
        return true;
    }

    static bool TryReadRequiredNumber(
        ElementContext context, JsonElement raw, string attr,
        out double value, [NotNullWhen(false)] out string? reason)
    {
        value = default;
        reason = null;

        if (!raw.TryGetProperty(attr, out var property))
        {
            reason = $"icon '{context.IconName}' element #{context.Index} (tag '{context.Tag}') is missing required attribute '{attr}'";
            return false;
        }

        return TryReadFiniteNumber(context, attr, property, out value, out reason);
    }

    static bool TryReadOptionalNumber(
        ElementContext context, JsonElement raw, string attr,
        out double? value, [NotNullWhen(false)] out string? reason)
    {
        value = null;
        reason = null;

        if (!raw.TryGetProperty(attr, out var property))
            return true;

        if (!TryReadFiniteNumber(context, attr, property, out var candidate, out reason))
            return false;

        value = candidate;
        return true;
    }

    /// <summary>Shared leaf of both numeric readers above — a JSON number token whose <see
    /// cref="double"/> value is finite (SPEC scope note: reject NaN/Infinity — reachable via a
    /// syntactically valid but magnitude-overflowing JSON literal like <c>1e400</c>, not just a
    /// literal <c>NaN</c> token). Also owns the one failure-message ternary both callers above used to
    /// hold a separate verbatim copy of (PLAN T302 review F6).</summary>
    static bool TryReadFiniteNumber(
        ElementContext context, string attr, JsonElement property,
        out double value, [NotNullWhen(false)] out string? reason)
    {
        value = default;
        reason = null;

        if (property.ValueKind != JsonValueKind.Number)
        {
            reason = $"icon '{context.IconName}' element #{context.Index} (tag '{context.Tag}') attribute '{attr}' must be a numeric value";
            return false;
        }

        var candidate = property.GetDouble();
        if (!double.IsFinite(candidate))
        {
            reason = $"icon '{context.IconName}' element #{context.Index} (tag '{context.Tag}') attribute '{attr}' must be finite";
            return false;
        }

        value = candidate;
        return true;
    }

    static bool TryValidateColorAttr(
        ElementContext context, JsonElement raw, string attr,
        out string? value, [NotNullWhen(false)] out string? reason)
    {
        value = null;
        reason = null;

        if (!raw.TryGetProperty(attr, out var property))
            return true;

        if (property.ValueKind != JsonValueKind.String)
        {
            reason = $"icon '{context.IconName}' element #{context.Index} (tag '{context.Tag}') attribute '{attr}' must be a string";
            return false;
        }

        var text = property.GetString() ?? "";
        if (!IsColorToken(text))
        {
            reason = $"icon '{context.IconName}' element #{context.Index} (tag '{context.Tag}') attribute '{attr}' must be 'none' or 'currentColor', not '{text}' — literal colors are inexpressible by schema";
            return false;
        }

        value = text;
        return true;
    }

    static bool IsColorToken(string value) => value is "none" or "currentColor";

    static ElementSchema BuildSchema(
        IReadOnlyList<string> requiredNumeric, IReadOnlyList<string> optionalNumeric, (string Attr, Regex Pattern)? grammar,
        ElementFactory factory)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "tag", "fill", "stroke" };
        allowed.UnionWith(requiredNumeric);
        allowed.UnionWith(optionalNumeric);
        if (grammar is { } g)
            allowed.Add(g.Attr);

        return new ElementSchema(requiredNumeric, optionalNumeric, grammar, allowed, factory);
    }

    /// <summary>Context threaded through one element's validation, purely to keep every failure
    /// message naming the exact icon/element/tag without re-passing three separate parameters through
    /// every helper (house "group past 3 params" convention).</summary>
    readonly record struct ElementContext(string IconName, int Index, string Tag = "");

    /// <summary>Constructs the closed <see cref="IconElement"/> for one already-validated tag — carried
    /// on <see cref="ElementSchema"/> itself (PLAN T302 review F3) so <see cref="ElementSchemas"/> is
    /// the SINGLE source enumerating the seven primitive tags: there is no longer a second,
    /// independently-enumerated tag switch that could drift from it, nor a runtime
    /// <see cref="System.Diagnostics.UnreachableException"/> guarding a tag the switch forgot.
    /// <paramref name="grammarValue"/> is <c>""</c>, never read, for every tag whose own
    /// <see cref="ElementSchema.Grammar"/> is <see langword="null"/>.</summary>
    delegate IconElement ElementFactory(
        IReadOnlyDictionary<string, double> numbers, string grammarValue, string? fill, string? stroke);

    /// <summary>The closed attribute shape one primitive tag admits — built once per tag by
    /// <see cref="BuildSchema"/>, not re-derived per element.</summary>
    sealed record ElementSchema(
        IReadOnlyList<string> RequiredNumericAttrs,
        IReadOnlyList<string> OptionalNumericAttrs,
        (string Attr, Regex Pattern)? Grammar,
        IReadOnlySet<string> AllowedAttributeNames,
        ElementFactory Factory);

    /// <summary>Ephemeral, all-nullable projection of the untrusted top-level document — mirrors
    /// <c>Theming.ThemeManifestParser</c>'s own <c>*Json</c> idiom: nothing here is trusted until
    /// checked field by field above, then discarded in favour of the immutable
    /// <see cref="IconPackDefinition"/>.</summary>
    sealed record IconPackDocumentJson
    {
        public IconPackStyleJson? Style { get; init; }
        public Dictionary<string, List<JsonElement>>? Icons { get; init; }
    }

    /// <summary>Ephemeral projection of the raw <c>style</c> block.</summary>
    sealed record IconPackStyleJson
    {
        public double? StrokeWidth { get; init; }
        public string? Fill { get; init; }
    }
}
