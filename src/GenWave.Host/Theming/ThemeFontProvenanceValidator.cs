namespace GenWave.Host.Theming;

using System.Linq;

/// <summary>
/// Enforces SPEC F103.10 — a theme references fonts ONLY from the GenWave-vendored curated set
/// (<see cref="FontProvenanceCatalog"/>) — plus the per-theme byte ceiling FONTS.md documents (PLAN
/// T188, closing ARCHITECTURE "Theme system"'s two font TODOs, for the curated set).
///
/// <see cref="ThemeManifestParser"/> already pins the URL SHAPE (its <c>FontSrcPattern</c>: a plain
/// <c>/fonts/&lt;name&gt;.woff2</c> path, never an absolute URL or a "../" traversal) — what THIS
/// type checks is EXISTENCE: does that shape actually resolve to a face GenWave vendored? A
/// manifest naming <c>/fonts/nonexistent.woff2</c> passes the shape check today and would only ever
/// fail once a browser requests it and <c>FontEndpoints</c>' closed, literal-filename switch 404s
/// it — silently, per-visitor, long after the theme was accepted. This type moves that failure to
/// LOAD time, the same "not a request-time condition to route around" posture
/// <see cref="ThemeManifestParser"/>'s own remarks already state for every other shape check it owns.
///
/// <para>
/// <b>Placement (PLAN T188's own "decide placement… and state your reasoning").</b> Deliberately
/// NOT folded into <see cref="ThemeManifestParser.Parse"/> or <see cref="ThemeCatalog.Load"/>
/// themselves — both are the generic, format-SHAPE-only seam a large body of existing
/// fixture-driven specs already drive with synthetic, non-vendored font paths; nothing in that
/// contract ever claimed those paths resolve to a real file. Provenance is a narrower, additive
/// concern, called explicitly from exactly the two places a theme manifest actually ENTERS the
/// running system:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ThemeCatalog.LoadShipped"/> — every shipped manifest, validated
/// once at boot (the canary in <c>Program.cs</c>) and reused by
/// <see cref="ThemeCatalog.CreateForStation"/>'s own initial state, so a bad shipped manifest still
/// fails loudly before the process ever serves a request — the same authoring-bug posture
/// <see cref="ThemeCatalog"/>'s own remarks already state for structural validation.</description></item>
/// <item><description>The theme import route — the ONLY <c>station.theme</c> write path (SPEC
/// F103.6). Enforcing here means every row ever persisted already satisfies this rule, so
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/>'s shipped∪owner rebuild needs no SECOND check
/// of its own: re-validating provenance on every reload would add a new exception source to the
/// exact method the SPEC F102.7 offline floor depends on staying narrow (an unreachable/malformed
/// store, not manifest content already settled at import time). The worst case for a row that
/// somehow slipped through anyway (a hand-edited <c>station.theme</c> row — never possible through
/// this app's own write path) is a 404'd font asset; <c>FontEndpoints</c>' closed, literal-filename
/// switch cannot serve an arbitrary path regardless, so skipping the reload-time re-check trades
/// away no security surface, only page-weight/UX budget the import gate already
/// protects.</description></item>
/// </list>
/// The theme detail LIVE-PREVIEW route (<see cref="GenWave.Host.Api.ThemePreviewController"/>) now calls this
/// SAME validator too (Dean's directive 2026-08-05, "preview refuses what import refuses") — an
/// operator must never be sold a live preview of a theme the import route would go on to reject.
/// Nothing that route composes is ever stored or served station-wide, so the import gate above
/// remains the one place that guarantees every PERSISTED row satisfies SPEC F103.10; the preview
/// call is an additive, non-persisting check of the same rule against ephemeral input.
/// </summary>
public static class ThemeFontProvenanceValidator
{
    /// <summary>
    /// FONTS.md's documented per-theme byte ceiling — see that document's "Per-theme byte ceiling"
    /// section for the full measurement and rationale (five vendored faces since T189; the base
    /// pair plus at most ONE of the T189 additions fits — FONTS.md's "Pairing constraint"). Update
    /// FONTS.md and this constant together if the number ever changes.
    /// </summary>
    public const long PerThemeByteCeilingBytes = 200 * 1024;

    /// <summary>
    /// Validates <paramref name="theme"/>'s font asset srcs against <paramref name="vendoredFacesBySrc"/>
    /// and <paramref name="ceilingBytes"/>. <paramref name="vendoredFacesBySrc"/>/
    /// <paramref name="ceilingBytes"/> are parameters, not a read of <see cref="FontProvenanceCatalog.Default"/>/
    /// <see cref="PerThemeByteCeilingBytes"/> directly, so a test can prove the byte-ceiling path
    /// with a small fixture provenance record instead of editing the real one (PLAN T188's own "you
    /// may add a fake face entry in a TEST fixture provenance record, not the real one") — production
    /// callers pass those two members explicitly.
    /// </summary>
    /// <exception cref="ThemeManifestException">
    /// <paramref name="theme"/> references a font asset src missing from
    /// <paramref name="vendoredFacesBySrc"/> — naming the theme, the missing face(s), and the whole
    /// vendored set — or its distinct referenced faces' summed bytes exceed
    /// <paramref name="ceilingBytes"/> — naming the theme, the total, and the ceiling.
    /// </exception>
    public static void Validate(
        ThemeManifest theme,
        IReadOnlyDictionary<string, VendoredFontFace> vendoredFacesBySrc,
        long ceilingBytes)
    {
        var referencedSrcs = theme.Fonts.Display.Assets
            .Concat(theme.Fonts.Sans.Assets)
            .Select(asset => asset.Src)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = referencedSrcs.Where(src => !vendoredFacesBySrc.ContainsKey(src)).ToList();
        if (missing.Count > 0)
        {
            var vendoredSet = string.Join(", ", vendoredFacesBySrc.Keys.OrderBy(src => src, StringComparer.Ordinal));
            throw new ThemeManifestException(
                $"theme '{theme.Slug}' references font(s) outside GenWave's vendored curated set: " +
                $"{string.Join(", ", missing)} (vendored set: {vendoredSet})");
        }

        var totalBytes = referencedSrcs.Sum(src => vendoredFacesBySrc[src].Bytes);
        if (totalBytes > ceilingBytes)
            throw new ThemeManifestException(
                $"theme '{theme.Slug}' references {totalBytes} bytes of vendored fonts, over the " +
                $"{ceilingBytes}-byte per-theme ceiling (FONTS.md)");
    }
}
