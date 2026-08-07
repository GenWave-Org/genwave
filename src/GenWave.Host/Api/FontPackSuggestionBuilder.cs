using GenWave.Host.Catalog;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// SPEC F104.10, PLAN T205/T207 — the pack-slug suggestion half of a font-provenance refusal, lifted
/// out of <see cref="ThemesImportController"/> (where it lived as a private
/// <c>BuildUnvendoredFontDetailAsync</c> method) into ONE shared home both <c>station.theme</c> write
/// routes call (PLAN T207 review carry-in 2): <see cref="ThemesImportController.Import"/> and
/// <see cref="ThemesSaveAsOwnController.SaveAsOwn"/> both refuse a law-violating manifest with "the
/// import route's exact copy" (STORY-287 AC3) — true by CONSTRUCTION once both call this same method
/// to build the enrichment half of that copy, rather than each carrying its own, independently
/// maintained projection two call sites could silently drift apart on. <see cref="ImportProblems"/>
/// already holds the static PROSE half of that shared copy (<see cref="ImportProblems.UnvendoredFont"/>/
/// <see cref="ImportProblems.UnvendoredFontDetail"/>); this type is the CATALOG-LOOKUP half neither of
/// those pure functions can own, since building the pack-slug suggestion needs a live
/// <see cref="CatalogProxyService"/> round trip.
/// </summary>
internal static class FontPackSuggestionBuilder
{
    /// <summary>
    /// Enriches <paramref name="baseDetail"/> (<see cref="ThemeFontProvenanceValidator.Validate"/>'s
    /// own refusal message, verbatim) with a providing-pack suggestion for every referenced face
    /// missing from BOTH the vendored and installed sets, when the catalog index knows one. Runs ONLY
    /// on an already-decided failure path — never on every write attempt — so a manifest that clears
    /// provenance never pays for a catalog round trip it has no use for.
    ///
    /// <para>
    /// <paramref name="installedFacesBySrc"/> is a PARAMETER, not a fresh
    /// <see cref="InstalledFontCatalog.InstalledByteSizeBySrc"/> read of its own (PLAN T205 review
    /// note, closed at T207) — the caller already built this same snapshot once to drive
    /// <see cref="ThemeFontProvenanceValidator.Validate"/> itself; re-reading it here would allocate a
    /// second, redundant dictionary on every refusal for no benefit, since nothing between the two
    /// calls can change what is currently installed.
    /// </para>
    ///
    /// <para>
    /// <b>Fail soft, by construction (SPEC F104.10's own "index unreachable ⇒ 400 still names the face,
    /// just without a pack suggestion").</b> <see cref="CatalogProxyService.GetIndexAsync"/> already
    /// collapses "the catalog is genuinely offline" and "the catalog kill switch is off" (SPEC F90.1)
    /// into the SAME <see cref="CatalogIndexFetchResult.Unreachable"/> outcome — this method treats both
    /// identically, returning <paramref name="baseDetail"/> untouched: the theme is refused either way,
    /// the suggestion is best-effort, additive prose only, never a precondition for refusing.
    /// </para>
    /// </summary>
    public static async Task<string> BuildUnvendoredFontDetailAsync(
        ThemeManifest manifest, string baseDetail, IReadOnlyDictionary<string, long> installedFacesBySrc,
        CatalogProxyService catalogProxyService, CancellationToken ct)
    {
        var missingSrcs = ThemeFontProvenanceValidator.FindMissingSrcs(
            manifest, FontProvenanceCatalog.Default.BySrc, installedFacesBySrc);
        if (missingSrcs.Count == 0)
            return baseDetail; // a ceiling-only refusal — nothing missing to suggest a pack for

        if (await catalogProxyService.GetIndexAsync(ct) is not CatalogIndexFetchResult.Ok index)
            return baseDetail; // fail soft — see this method's own remarks

        // .woff2 only (PLAN T207 review carry-in 2) — a missing SRC is always a "/fonts/<name>.woff2"
        // path (ThemeManifestParser.FontSrcPattern pins that shape before a manifest ever parses), so a
        // font entry's OTHER declared asset (its OFL licence text) can never match FileFromSrc's lookup
        // key either way; filtering it out here keeps the projection built below limited to the files
        // this lookup could ever actually be asked about.
        var packSlugByFile = index.Entries
            .Where(entry => entry.Kind == CatalogEntryKind.Font)
            .SelectMany(entry => entry.Assets
                .Where(asset => asset.Path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
                .Select(asset => (File: Path.GetFileName(asset.Path), entry.Slug)))
            .GroupBy(pair => pair.File, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Slug, StringComparer.Ordinal);

        var providingPackSlugsByMissingSrc = missingSrcs
            .Where(src => packSlugByFile.ContainsKey(FileFromSrc(src)))
            .ToDictionary(src => src, src => packSlugByFile[FileFromSrc(src)], StringComparer.Ordinal);

        return ImportProblems.UnvendoredFontDetail(baseDetail, providingPackSlugsByMissingSrc);
    }

    // ThemeManifestParser.FontSrcPattern pins every font asset src to the fixed "/fonts/<name>.woff2"
    // shape before a manifest ever parses successfully — both callers only ever reach here past that
    // gate, so a plain substring slice is exact, not a best-effort Path.GetFileName the shape check
    // already makes unnecessary.
    static string FileFromSrc(string src) => src["/fonts/".Length..];
}
