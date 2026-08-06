namespace GenWave.Host.Theming;

using System.Diagnostics.CodeAnalysis;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Catalog;

/// <summary>
/// The in-memory, installed-faces view (SPEC F104.6/F104.8/F104.9; STORY-282/283; ARCHITECTURE.md
/// "Community Catalog v2 → wardrobe" component table) — vendored fonts stay the literal switch
/// <see cref="FontEndpoints"/> already carries; this catalog is the OTHER half of the widened closed
/// set, sourced from <see cref="IFontPackStore"/>. Three consumers, in build order: the widened
/// <c>GET /fonts/{file}</c> route (T200, this task — <see cref="TryGetFace"/>), the widened
/// <c>ThemeFontProvenanceValidator</c> (T205), and the editor's face pickers (T206).
///
/// <para>
/// <b>The volatile-snapshot idiom (mirrors <see cref="ThemeCatalog.state"/>'s own precedent, PLAN
/// T119 review F1's "singleton mutates its own cache in-place, readers never lock" shape).</b> A
/// request thread's <see cref="TryGetFace"/> call is a plain dictionary read against
/// <see cref="facesByFile"/> — never a store round-trip — so serving an installed face costs exactly
/// what serving a vendored one does. <see cref="ReloadAsync"/> is the ONE place that ever talks to
/// <see cref="store"/>: once at boot (<see cref="InstalledFontCatalogLoadHostedService"/>, the
/// <see cref="ThemeCatalogOwnerLoadHostedService"/> precedent) and again after every install
/// (<c>FontPackController</c>, the <c>ThemesImportController</c>/T184 "rebuild after write"
/// precedent) — never per-request. <c>volatile</c> so a reload racing a concurrent request always
/// hands that request either the old or the new reference in full, never a torn read.
/// </para>
///
/// <para>
/// <b>"Keep serving with catalog/DB gone once loaded" (SPEC F104.8) — a reload failure degrades,
/// never throws.</b> <see cref="ReloadAsync"/> swallows every exception fetching or rebuilding the
/// snapshot, WARN-logs once, and leaves <see cref="facesByFile"/> untouched — the boot case degrades
/// to empty (vendored-only, since <see cref="FontEndpoints"/>' literal switch is untouched by any
/// pack machinery — the two embedded themes' own SPEC F102.7 floor these faces might back is
/// similarly unreachable from this class), and a post-install reload failure degrades to whatever was
/// last loaded successfully. NOTE the deliberate difference from
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> (T200 review): ThemeCatalog RESETS to shipped
/// state on a failed reload (its floor is the two embedded themes); this catalog RETAINS the
/// last-good snapshot — F104.8's "installed faces keep serving with catalog/DB gone once loaded"
/// demands retention, not reset.
/// </para>
///
/// <para>
/// <b>Memory math.</b> <c>FontPackController.MaxPackBytes</c> (200 KiB) bounds one installed pack's
/// summed face bytes; this is Dean-only curation (FONTS.md), not a community-scale surface, but even
/// at <see cref="CatalogProxyService"/>'s own established "64 slots" bound (its <c>MaxCachedAssets</c>
/// remarks) for a comparable admin-curated set, 64 packs × 200 KiB is a 12.5 MiB worst case fully
/// resident — trivial for a long-lived singleton, and a fraction of what that same 64-slot bound
/// already accepts for the single-file 256 KiB ceiling it uses instead.
/// </para>
///
/// <para>
/// <b>REVIEWER OBLIGATION, carried forward from <c>FontPackController</c> (T199 review) — READ BEFORE
/// putting <see cref="FontPack.Family"/> or <see cref="FontPackFace.Style"/> into a stylesheet.</b>
/// Both are stored VERBATIM, UNBOUNDED (no CSS-safe shape/length gate — <c>FontPackController</c>'s
/// own remarks explain why not: T199 shipped no consumer that reads either back into CSS). This class
/// exposes neither today — <see cref="TryGetFace"/> returns raw bytes by file name and interpolates
/// nothing into CSS, so T200 passes this obligation through untouched. Whichever future consumer of
/// THIS class first reaches for <c>Family</c>/<c>Style</c> in a stylesheet context (T203's library
/// page, T206's editor pickers) MUST NOT trust them as CSS-safe merely because they came from here —
/// apply the same bound+shape discipline <c>CatalogIndexValidator.TryParseFamily</c> already
/// established for the index-side field, or an equivalent CSS-injection-safe escape/allowlist, first.
/// </para>
/// </summary>
public sealed class InstalledFontCatalog
{
    readonly IFontPackStore store;
    readonly ILogger<InstalledFontCatalog> logger;

    /// <summary>The set every <see cref="TryGetFace"/> call actually serves — empty until the first
    /// successful <see cref="ReloadAsync"/> (never <see langword="null"/>, so a request racing boot
    /// before the hosted-service warm-up completes gets a clean miss, not a null-reference). See this
    /// class's own remarks for the volatile-snapshot/offline-floor rationale.</summary>
    volatile IReadOnlyDictionary<string, FontPackFaceContent> facesByFile =
        new Dictionary<string, FontPackFaceContent>(StringComparer.Ordinal);

    InstalledFontCatalog(IFontPackStore store, ILogger<InstalledFontCatalog> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    /// <summary>Builds the runtime, DI-registered instance — reads nothing from <paramref name="store"/>
    /// yet (mirrors <see cref="ThemeCatalog.CreateForStation"/>'s own "resolving is never enough to
    /// connect" rule): the returned instance serves an empty (vendored-only) set until a caller awaits
    /// <see cref="ReloadAsync"/>.</summary>
    public static InstalledFontCatalog Create(IFontPackStore store, ILogger<InstalledFontCatalog> logger) =>
        new(store, logger);

    /// <summary>
    /// The widened <c>GET /fonts/{file}</c> route's (T200) hot path once a request falls through
    /// <see cref="FontEndpoints"/>' vendored literal switch — a plain, synchronous lookup against the
    /// current snapshot, never a per-request store read (see this class's own remarks).
    /// </summary>
    public bool TryGetFace(string file, [NotNullWhen(true)] out FontPackFaceContent? content) =>
        facesByFile.TryGetValue(file, out content);

    /// <summary>
    /// Rebuilds the installed-faces snapshot from <see cref="store"/> — called once at boot
    /// (<see cref="InstalledFontCatalogLoadHostedService"/>) and again after every successful install
    /// (<c>FontPackController</c>, with <see cref="CancellationToken.None"/> — the T184 rebuild-after-
    /// write lesson: a committed write's own rebuild is no longer the request's to abandon).
    ///
    /// <para>
    /// Two calls per face, by construction of <see cref="IFontPackStore"/>'s own seam:
    /// <see cref="IFontPackStore.GetAllAsync"/> lists every installed pack's faces (metadata only, no
    /// bytes — that store method's own remarks), then <see cref="IFontPackStore.GetFaceByFileAsync"/>
    /// fetches each one's bytes in turn. Bounded by how many faces are actually installed (see this
    /// class's own memory-math remarks) and run only at boot/reload, never per request — the N+1 shape
    /// here is not a hot-path concern the way it would be if this ran per <see cref="TryGetFace"/> call.
    /// </para>
    ///
    /// <para>
    /// Never throws (SPEC F104.8): any failure — an unreachable store, a face
    /// <see cref="IFontPackStore.GetAllAsync"/> just listed disappearing before its own
    /// <see cref="IFontPackStore.GetFaceByFileAsync"/> call resolves (a rare TOCTOU race, not an
    /// error) — is WARN-logged and leaves <see cref="facesByFile"/> exactly as it was, degrading to
    /// the last-good snapshot (or the empty, vendored-only floor if this is the very first load).
    /// </para>
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            var packs = await store.GetAllAsync(ct);
            var snapshot = new Dictionary<string, FontPackFaceContent>(StringComparer.Ordinal);
            foreach (var pack in packs)
            {
                foreach (var face in pack.Faces)
                {
                    var content = await store.GetFaceByFileAsync(face.File, ct);
                    if (content is null)
                    {
                        // TOCTOU: GetAllAsync just listed this face, but it is gone by the time this
                        // call resolves (e.g. a concurrent uninstall — PLAN T208). Not a load fault —
                        // skip it; the reload that follows the uninstall will reflect the true set.
                        continue;
                    }

                    snapshot[face.File] = content;
                }
            }

            facesByFile = snapshot;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "installed font catalog reload failed — serving continues from the last-good snapshot (SPEC F104.8 offline floor)");
        }
    }
}
