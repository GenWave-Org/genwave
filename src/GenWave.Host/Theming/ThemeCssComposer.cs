namespace GenWave.Host.Theming;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Composes one theme into CSS text — the STORY-264 composed stylesheet <c>GET /spectator/theme.css</c>
/// (T160) and <c>GET /api/theme.css</c> (T161) serve verbatim as <c>text/css</c>. This is the body of a
/// SERVED response, never an inlined <c>&lt;style&gt;</c> block: <c>style-src 'self'</c> ships today and a
/// same-origin stylesheet needs no CSP change, where inlining would need <c>'unsafe-inline'</c>
/// (ARCHITECTURE "Theme system", gh-#180).
///
/// Emits <c>@font-face</c> rules for the ACTIVE theme's faces only (SPEC F102.4), mode-independent so
/// they appear once — with 6+ themes each able to carry their own faces, emitting every shelf theme's
/// fonts in one sheet would be ruinous page weight, and a theme's fonts must never be downloaded while
/// another theme is active.
///
/// Then BOTH of the theme's modes, as the exact three selector blocks
/// <c>admin-ui/app/globals.css</c> already defines (globals.css:58,106,139-140) — <c>:root</c> (light,
/// the default), <c>:root[data-theme="dark"]</c> (an explicit dark choice) and
/// <c>@media (prefers-color-scheme: dark) { :root:not([data-theme]) }</c> (the OS default, when nobody
/// has chosen). This is not a style choice: <c>Compose</c> was originally specified as taking an
/// already-resolved <c>(theme, mode)</c> pair and emitting one flat <c>:root</c>, but that shape is
/// unsatisfiable — F102.10 forbids a new spectator network call, so the server can never learn a
/// visitor's OS preference, yet F102.13 requires <c>prefers-color-scheme</c> to still pick the mode.
/// The composed sheet has to carry both modes and let the browser's cascade decide, exactly like the
/// static default already does. Matching the static sheets' selectors byte-for-byte is what makes a
/// later-loading composed sheet override them cleanly: a flat single-mode <c>:root</c> would either tie
/// the static spectator <c>@media</c> block's specificity — (0,1,0) each — stranding an OS-dark visitor
/// in the composed LIGHT sheet (precisely what F102.13 forbids), or lose outright to the static admin
/// blocks' higher-specificity attribute selectors — (0,2,0) versus a flat <c>:root</c>'s (0,1,0) — so a
/// themed dark admin page would show the DEFAULT's dark tokens instead of the theme's. Values are
/// duplicated between the explicit-dark and system-dark blocks rather than shared, matching
/// globals.css's own duplication (globals.css:135, SPEC F28.4): CSS custom properties have no
/// block-reuse mechanism, so there is no way to declare a dark token set once and have both selectors
/// pick it up.
///
/// Mode RESOLUTION — which theme is active, and which selector state the current PAGE stamps (an
/// explicit <c>data-theme</c>, or leaving it absent for the OS default) — stays T164's job; this type
/// takes no catalog and no request context, so AC5 ("only the active theme's fonts, never a whole
/// shelf's worth") holds structurally rather than by caller discipline.
/// </summary>
public static partial class ThemeCssComposer
{
    // Token NAMES are shape-checked at load (ThemeManifestParser.ValidateTokenNames, review
    // finding, T159 round 2) using this exact pattern, so by the time a ThemeManifest reaches this
    // composer every name is already known-safe. Kept here anyway as belt-and-braces, not as the
    // primary gate: a token name becomes the identifier half of a CSS custom-property declaration
    // this composer emits verbatim (`--{name}: {value};`), and CSS's forgiving parser means an
    // unanchored name could break out of that declaration — e.g. a name containing "}" closes the
    // enclosing block early, letting the remainder of the "name" open a fresh rule in the same
    // served response. Fail closed here too, matching every other shape check in this feature
    // (ThemeManifestParser never sanitizes, only rejects): a name outside this allow-list throws
    // rather than being escaped into the response.
    [GeneratedRegex(@"\A[a-z][a-z0-9-]*\z")]
    private static partial Regex TokenNamePattern();

    /// <summary>
    /// Renders <paramref name="theme"/>'s active <c>@font-face</c> rules plus all three token
    /// blocks (light, explicit dark, system-default dark) as CSS text.
    /// </summary>
    public static string Compose(ThemeManifest theme)
    {
        var css = new StringBuilder();
        AppendFontFace(css, theme.Fonts.Display);
        AppendFontFace(css, theme.Fonts.Sans);
        AppendLightBlock(css, theme);
        AppendExplicitDarkBlock(css, theme);
        AppendSystemDarkBlock(css, theme);
        return css.ToString();
    }

    static void AppendFontFace(StringBuilder css, ThemeFontFace face)
    {
        foreach (var asset in face.Assets)
        {
            css.Append("@font-face {\n");
            css.Append("  font-family: \"").Append(face.Family).Append("\";\n");
            css.Append("  src: url(\"").Append(asset.Src).Append("\") format(\"woff2\");\n");
            css.Append("  font-weight: ").Append(asset.Weight).Append(";\n");
            css.Append("  font-style: ").Append(asset.Style).Append(";\n");
            css.Append("  font-display: swap;\n");
            css.Append("}\n\n");
        }
    }

    // :root — light tokens, the default when nobody has made an explicit choice and the OS has no
    // (or reports no) dark preference. Matches globals.css:58 exactly. Font stacks are declared
    // here only (ARCHITECTURE "Theme system": "--font-display and --font-sans are also declared in
    // :root today but are NOT tokens under this design") — not mode-dependent, so they'd be pure
    // duplication in the dark blocks below, exactly as globals.css:100-103 itself argues.
    static void AppendLightBlock(StringBuilder css, ThemeManifest theme)
    {
        css.Append(":root {\n");
        AppendTokenDeclarations(css, theme, "light", theme.Modes.Light, "  ");
        css.Append("  --font-display: \"").Append(theme.Fonts.Display.Family).Append("\", Georgia, serif;\n");
        css.Append("  --font-sans: \"").Append(theme.Fonts.Sans.Family).Append("\", system-ui, sans-serif;\n");
        css.Append("}\n\n");
    }

    // :root[data-theme="dark"] — dark tokens, an explicit choice (cookie/setting). Higher
    // specificity than the flat :root above, so it always wins regardless of source order. Matches
    // globals.css:106 exactly.
    static void AppendExplicitDarkBlock(StringBuilder css, ThemeManifest theme)
    {
        css.Append(":root[data-theme=\"dark\"] {\n");
        AppendTokenDeclarations(css, theme, "dark", theme.Modes.Dark, "  ");
        css.Append("}\n\n");
    }

    // @media (prefers-color-scheme: dark) { :root:not([data-theme]) } — dark tokens again, for the
    // OS-default case: nobody has made an explicit choice, so the browser's own media query alone
    // selects this block (F102.13) without any request ever reaching the server for it (F102.10).
    // Matches globals.css:139-140 exactly, values duplicated against the explicit-dark block above
    // rather than shared (globals.css:135, SPEC F28.4 — CSS custom properties have no block-reuse
    // mechanism).
    static void AppendSystemDarkBlock(StringBuilder css, ThemeManifest theme)
    {
        css.Append("@media (prefers-color-scheme: dark) {\n");
        css.Append("  :root:not([data-theme]) {\n");
        AppendTokenDeclarations(css, theme, "dark", theme.Modes.Dark, "    ");
        css.Append("  }\n");
        css.Append("}\n");
    }

    static void AppendTokenDeclarations(
        StringBuilder css, ThemeManifest theme, string modeLabel, IReadOnlyDictionary<string, string> tokens, string indent)
    {
        foreach (var (name, value) in tokens)
        {
            if (!TokenNamePattern().IsMatch(name))
            {
                throw new ThemeManifestException(
                    $"theme '{theme.Slug}' mode '{modeLabel}' has a token name '{name}' outside the safe custom-property shape");
            }

            css.Append(indent).Append("--").Append(name).Append(": ").Append(value).Append(";\n");
        }
    }
}
