namespace GenWave.Host.Theming;

using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Parses and validates ONE theme manifest document against STORY-263's load-time rules: slug,
/// name and author present; both font roles declared with a family and at least one vendored asset
/// each; both <c>light</c> and <c>dark</c> modes present (AC6); and the two modes carry the exact
/// same set of token keys (AC8). <see cref="ThemeCatalog.Load"/> is the only caller — duplicate
/// slugs across the whole shipped set (AC7) are a catalog-level concern, checked there, because a
/// single manifest can never know about its siblings.
///
/// Every failure throws a <see cref="ThemeManifestException"/> naming the theme (its slug once
/// known, otherwise its origin label) and, where the failure is mode- or token-scoped, the mode and
/// the token too — "invalid theme" alone never reaches a caller.
///
/// Beyond presence, every font descriptor and token VALUE — and, as of a T159 review fix, every
/// token NAME too — is checked against a conservative CSS-safe shape: <c>ThemeCssComposer</c>
/// (T159) interpolates all of these straight into CSS served same-origin to both admin and
/// spectator, so an unshaped value OR name is a same-origin CSS-injection primitive — low
/// severity today (Layer A ships only first-party embedded manifests, no write path) but high
/// once Layer B (gh-#206) accepts manifests from a community catalog, since the format is
/// deliberately identical either way. The name check belongs HERE, at load, rather than only in
/// the composer that happens to interpolate it: a malformed manifest must never become a
/// normally-loadable theme that only fails once something composes it (<see cref="ThemeCatalog"/>'s
/// own remarks: "not a request-time condition to route around"). Token-set MEMBERSHIP against a
/// fixed vocabulary — whether the names present are actually the format's fixed canon, as opposed
/// to merely shaped like a safe identifier — is a separate, explicitly out-of-scope concern; see
/// the remark in <see cref="ParseModes"/>.
///
/// <para>
/// The nested <c>*Json</c> records are an ephemeral, all-nullable projection of the untrusted
/// document — mirrors <see cref="Catalog.CatalogIndexValidator"/>'s own idiom: nothing here is
/// trusted until checked field by field, then discarded in favour of the immutable
/// <see cref="ThemeManifest"/> domain type.
/// </para>
/// </summary>
internal static partial class ThemeManifestParser
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Every vendored font asset ships under wwwroot's /fonts/ tree as a single pre-built woff2 file
    // — no query string, no nested directory, no other format. Anchored so it can only ever match a
    // plain relative asset path, never an absolute URL or "../" traversal (the shape idiom
    // <see cref="Catalog.CatalogIndexValidator"/> uses for SPEC F90.2, applied here to the theme
    // format's own write-adjacent surface).
    [GeneratedRegex(@"\A/fonts/[a-z0-9-]+\.woff2\z")]
    private static partial Regex FontSrcPattern();

    // font-family names in this codebase's own vendored set ("Fraunces", "Source Sans 3") — letters,
    // digits, spaces and hyphens only. Rejects the quote/brace/semicolon characters a CSS-injection
    // payload needs to escape the `font-family: "<value>"` position T159 interpolates it into.
    //
    // `internal const` (T194 review finding — the exact TokenValueText precedent immediately below,
    // applied to family instead of colour): CatalogIndexValidator's own community-catalog
    // shelf-card family name (SPEC F104.3/STORY-281 AC1) is the SAME data class — a font's own
    // CSS-interpolated family string — just arriving off an untrusted index.json `family` field
    // instead of a manifest, and reaching CatalogShelfEntryDto.FontFamily (and, downstream, an
    // inline CSS `style`/`font-family` position in the Admin UI) unvalidated would let a hostile
    // value (e.g. `'X;}</style><script>alert(1)</script>'`) through; that class must reject exactly
    // what this parser rejects rather than carry a second, independently-drifting copy of the shape.
    // `const` (not `static readonly`), like `TokenValueText`, because `[GeneratedRegex]` attribute
    // arguments must be compile-time constants.
    internal const string FontFamilyText = @"\A[A-Za-z0-9][A-Za-z0-9 -]*\z";

    [GeneratedRegex(FontFamilyText)]
    private static partial Regex FontFamilyPattern();

    // font-weight ("400", "normal", or a variable-font range like "400 600") and font-style
    // ("normal", "italic", "oblique 10deg") share one conservative shape: letters, digits, spaces
    // and a decimal point only — enough for every legitimate @font-face descriptor value, nothing
    // that can close a CSS declaration.
    [GeneratedRegex(@"\A[A-Za-z0-9 .]+\z")]
    private static partial Regex FontDescriptorPattern();

    // Token VALUES are, per ARCHITECTURE's own vocabulary, ALL colours — the 13 semantic names plus
    // the 6 `--sched-*` swatches, nothing else (font-family fallback lists live in the `fonts`
    // object, not here). That domain is narrow, so — matching Src/Family/Weight/Style above — this
    // is an anchored allow-list, not a denylist: a `#` sign followed by 3-8 hex digits, the only
    // shape today's shipped and fixture manifests ever use. rgb()/hsl()/oklch() forms are
    // deliberately not accepted; widen this only against a concrete manifest that needs one.
    //
    // `internal const` (review finding, T185): CatalogIndexValidator's own community-catalog
    // shelf-preview swatches (SPEC F103.4) are the SAME data class — a theme's hex colour tokens —
    // just arriving off an untrusted index.json instead of a manifest, and reaching the wire (and
    // an inline `style` attribute in the Admin UI) unvalidated would let a hostile swatch string
    // (e.g. `'red;background-image:url(https://evil/x)'`) through; that class must reject exactly
    // what this parser rejects rather than carry a second, independently-drifting copy of the
    // shape. `const` (not `static readonly`), like `CatalogIndexValidator.SlugSegment`, because
    // `[GeneratedRegex]` attribute arguments must be compile-time constants.
    internal const string TokenValueText = @"\A#[0-9a-fA-F]{3,8}\z";

    [GeneratedRegex(TokenValueText)]
    private static partial Regex TokenValuePattern();

    // Token NAMES become the identifier half of a CSS custom-property declaration
    // ThemeCssComposer emits verbatim (`--{name}: {value};`) — the same CSS-injection concern as
    // TokenValuePattern above, but for the key rather than the value (review finding, T159 round
    // 2: this must be caught HERE, at load, not only if/when a caller composes the theme — see
    // this type's own remarks). Lowercase letters, digits and hyphens, starting with a letter —
    // enough for every token this format's fixed vocabulary actually uses ("bg", "accent-ink",
    // "sched-1", …), nothing that can close a CSS declaration early. ThemeCssComposer enforces
    // this exact same shape a second time as belt-and-braces, not as a substitute for this check.
    [GeneratedRegex(@"\A[a-z][a-z0-9-]*\z")]
    private static partial Regex TokenNamePattern();

    public static ThemeManifest Parse(ThemeManifestSource source)
    {
        ThemeManifestJson? document;
        try
        {
            document = JsonSerializer.Deserialize<ThemeManifestJson>(source.Json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ThemeManifestException($"theme manifest '{source.Name}' is malformed JSON ({ex.Message})");
        }

        if (document is null)
            throw new ThemeManifestException($"theme manifest '{source.Name}' is empty");

        if (document.Slug is not { Length: > 0 } slug)
            throw new ThemeManifestException($"theme manifest '{source.Name}' is missing a slug");

        if (document.Name is not { Length: > 0 } name)
            throw new ThemeManifestException($"theme '{slug}' is missing a name");

        if (document.Author is not { Length: > 0 } author)
            throw new ThemeManifestException($"theme '{slug}' is missing an author");

        var fonts = ParseFonts(slug, document.Fonts);
        var modes = ParseModes(slug, document.Modes);

        return new ThemeManifest(slug, name, author, fonts, modes);
    }

    static ThemeFonts ParseFonts(string slug, ThemeFontsJson? raw)
    {
        if (raw is null)
            throw new ThemeManifestException($"theme '{slug}' is missing its font declarations");

        return new ThemeFonts(
            ParseFontFace(slug, "display", raw.Display),
            ParseFontFace(slug, "sans", raw.Sans));
    }

    static ThemeFontFace ParseFontFace(string slug, string role, ThemeFontFaceJson? raw)
    {
        if (raw is not { Family: { Length: > 0 } family })
            throw new ThemeManifestException($"theme '{slug}' font '{role}' is missing a family");

        if (!FontFamilyPattern().IsMatch(family))
            throw new ThemeManifestException($"theme '{slug}' font '{role}' has an invalid family '{family}'");

        if (raw.Assets is not { Count: > 0 } rawAssets)
            throw new ThemeManifestException($"theme '{slug}' font '{role}' declares no vendored assets");

        var assets = new List<ThemeFontAsset>(rawAssets.Count);
        foreach (var rawAsset in rawAssets)
        {
            if (rawAsset is not { Src: { Length: > 0 } src, Weight: { Length: > 0 } weight, Style: { Length: > 0 } style })
                throw new ThemeManifestException($"theme '{slug}' font '{role}' has an asset missing src/weight/style");

            if (!FontSrcPattern().IsMatch(src))
                throw new ThemeManifestException($"theme '{slug}' font '{role}' has an asset with an invalid src '{src}'");

            if (!FontDescriptorPattern().IsMatch(weight))
                throw new ThemeManifestException($"theme '{slug}' font '{role}' has an asset with an invalid weight '{weight}'");

            if (!FontDescriptorPattern().IsMatch(style))
                throw new ThemeManifestException($"theme '{slug}' font '{role}' has an asset with an invalid style '{style}'");

            assets.Add(new ThemeFontAsset(src, weight, style));
        }

        return new ThemeFontFace(family, assets);
    }

    static ThemeModes ParseModes(string slug, ThemeModesJson? raw)
    {
        if (raw?.Light is not { Count: > 0 } light)
            throw new ThemeManifestException($"theme '{slug}' is missing its 'light' mode");

        if (raw.Dark is not { Count: > 0 } dark)
            throw new ThemeManifestException($"theme '{slug}' is missing its 'dark' mode");

        ValidateTokenValues(slug, "light", light);
        ValidateTokenValues(slug, "dark", dark);
        ValidateTokenNames(slug, "light", light);
        ValidateTokenNames(slug, "dark", dark);

        // Membership PARITY between the two modes is checked below (AC8: neither mode may define a
        // token the other lacks). Token-set VOCABULARY — whether the names present ("bg", "ink",
        // "accent", …) are actually the format's fixed 19-name canon — is deliberately NOT checked
        // here (that's distinct from the CHARACTER-SET shape ValidateTokenNames above just
        // enforced: a name can be a well-formed identifier and still not belong to the canon);
        // T156's task scope enumerates exactly the three checks in this method, and vocabulary
        // reaches into T158's data-driven AA gate. That gap has a real, silent failure mode: a
        // manifest omitting "accent-ink" from BOTH modes loads clean (parity holds — neither side has
        // it), and the static-stylesheet fallback then silently paints cats-whisker's accent-ink over
        // this theme's own accent. It renders fine, so nobody sees it — exactly the "3.9:1 pair in
        // theme #5" failure SPEC F102.8 exists to catch. T158 is being updated to require vocabulary
        // validation against the 19-name canon.
        foreach (var token in light.Keys)
        {
            if (!dark.ContainsKey(token))
                throw new ThemeManifestException(
                    $"theme '{slug}' mode 'dark' is missing token '{token}' that its 'light' mode defines");
        }

        foreach (var token in dark.Keys)
        {
            if (!light.ContainsKey(token))
                throw new ThemeManifestException(
                    $"theme '{slug}' mode 'light' is missing token '{token}' that its 'dark' mode defines");
        }

        return new ThemeModes(light, dark);
    }

    static void ValidateTokenValues(string slug, string mode, IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var (token, value) in tokens)
        {
            if (string.IsNullOrEmpty(value) || !TokenValuePattern().IsMatch(value))
                throw new ThemeManifestException(
                    $"theme '{slug}' mode '{mode}' token '{token}' has an invalid value '{value}'");
        }
    }

    static void ValidateTokenNames(string slug, string mode, IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var name in tokens.Keys)
        {
            if (!TokenNamePattern().IsMatch(name))
                throw new ThemeManifestException(
                    $"theme '{slug}' mode '{mode}' has a token name '{name}' outside the safe custom-property shape");
        }
    }

    /// <summary>Ephemeral JSON projection of the untrusted theme manifest document.</summary>
    sealed record ThemeManifestJson
    {
        public string? Slug { get; init; }
        public string? Name { get; init; }
        public string? Author { get; init; }
        public ThemeFontsJson? Fonts { get; init; }
        public ThemeModesJson? Modes { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw manifest's <c>fonts</c> object.</summary>
    sealed record ThemeFontsJson
    {
        public ThemeFontFaceJson? Display { get; init; }
        public ThemeFontFaceJson? Sans { get; init; }
    }

    /// <summary>Ephemeral JSON projection of one raw <c>display</c>/<c>sans</c> font role.</summary>
    sealed record ThemeFontFaceJson
    {
        public string? Family { get; init; }
        public IReadOnlyList<ThemeFontAssetJson>? Assets { get; init; }
    }

    /// <summary>Ephemeral JSON projection of one raw vendored font asset.</summary>
    sealed record ThemeFontAssetJson
    {
        public string? Src { get; init; }
        public string? Weight { get; init; }
        public string? Style { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw manifest's <c>modes</c> object.</summary>
    sealed record ThemeModesJson
    {
        public IReadOnlyDictionary<string, string>? Light { get; init; }
        public IReadOnlyDictionary<string, string>? Dark { get; init; }
    }
}
