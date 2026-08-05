namespace GenWave.Host.Theming;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Loads and validates the station's theme manifests, then exposes them by slug (STORY-263). Layer
/// A's shipped manifests are embedded resources in this assembly (<see cref="LoadShippedSources"/>,
/// ARCHITECTURE "Theme system"); Layer B's owner-imported manifests live in <c>station.theme</c>
/// (<see cref="IThemeStore"/>, STORY-271, PLAN T181/T182) and go through the exact same
/// <see cref="Load"/>/<c>ThemeManifestParser</c> path as the embedded ones — the manifest format
/// makes no distinction between the two (STORY-263 AC4), so neither does loading it.
///
/// Validation happens once, at load, via <c>ThemeManifestParser</c> plus this class's own
/// duplicate-slug check across the whole set. Whichever set a given <see cref="ThemeCatalog"/>
/// instance is currently serving therefore either loaded fully valid, or the load never took effect —
/// never a set that silently dropped a bad theme. A malformed SHIPPED manifest is a build-time
/// authoring bug the operator must fix, not a request-time condition to route around, so it still
/// throws <see cref="ThemeManifestException"/> naming the offender rather than dropping it — see
/// <see cref="LoadShipped"/>'s own remarks. A malformed OWNER row (or an unreachable/empty
/// <c>station.theme</c> store) is a different failure class entirely — see
/// <see cref="ReloadOwnerThemesAsync"/>'s own remarks on the SPEC F102.7 offline floor.
/// </summary>
public sealed class ThemeCatalog
{
    /// <summary>ARCHITECTURE "Theme system": "shipped default <c>cats-whisker</c>" (SPEC F102.5).
    /// Lives here — not on either serving surface — because both the spectator route (T160) and the
    /// admin route (T161) need the exact same slug: two surfaces reading one constant off the
    /// catalog that owns the manifests, rather than one surface restating the other's literal or
    /// depending on its type. <see cref="TryGetBySlug"/> only guarantees a slug that IS in the
    /// catalog resolves — not that this particular one is among them — so callers that rely on this
    /// being present (both endpoint modules today) assert it once at boot rather than discovering a
    /// gap per-request; see <c>Program.cs</c>'s own startup assertion.</summary>
    public const string ShippedDefaultSlug = "cats-whisker";

    /// <summary>
    /// The visitor cookie theme resolution reads (SPEC F102.5, PLAN T164). Names the THEME
    /// (palette) slug — the independent sibling of admin-ui's <c>genwave-mode</c> cookie, which
    /// names the light/dark MODE within whichever theme is active (PLAN T164 ruling, 2026-08-03:
    /// two axes, two cookies, deliberately never conflated). Nothing in <see cref="Resolve"/> ever
    /// WRITES this cookie — the switcher that does (PLAN T166) reads this same constant so the two
    /// surfaces can never drift on the name.
    /// </summary>
    public const string CookieName = "genwave-theme";

    /// <summary>Every embedded resource this assembly ships under this segment, ending in
    /// <c>.json</c>, is treated as a shipped theme manifest (ARCHITECTURE "Theme system":
    /// <c>themes/*.json</c>, embedded resources in GenWave.Host).</summary>
    const string ShippedResourceSegment = ".Theming.themes.";

    /// <summary>
    /// The fixed embedded set this instance was constructed with — never rebuilt after construction
    /// (embedded resources cannot change at runtime). Two jobs: the floor <see cref="ReloadOwnerThemesAsync"/>
    /// falls back to on any owner-load failure (SPEC F102.7), and the slug vocabulary it checks a
    /// candidate owner row against before ever admitting it (SPEC F103.8, "shipped slugs reserved").
    /// </summary>
    readonly CatalogState shippedState;

    /// <summary>
    /// Present only for a <see cref="CreateForStation"/>-built instance — <see langword="null"/> for
    /// every <see cref="Load"/>/<see cref="LoadShipped"/> instance (StationSettingsAllowlist's own
    /// copy, every fixture-driven test, and <c>Program.cs</c>'s own boot-time canary check), none of
    /// which ever calls <see cref="ReloadOwnerThemesAsync"/>. Kept as one nullable pair rather than
    /// two independently-nullable fields so "has a store" and "has a logger to report through" can
    /// never disagree — see <see cref="ReloadOwnerThemesAsync"/>.
    /// </summary>
    readonly (IThemeStore Store, ILogger<ThemeCatalog> Logger)? ownerLoad;

    /// <summary>
    /// The set every read (<see cref="All"/>, <see cref="TryGetBySlug"/>, <see cref="Resolve"/>)
    /// actually serves — shipped-only until/unless <see cref="ReloadOwnerThemesAsync"/> has folded
    /// owner rows in. <c>volatile</c>: a request thread reading this field concurrently with a
    /// <see cref="ReloadOwnerThemesAsync"/> call running on a hosted service (boot warm-up) or an
    /// import request (T184) must see either the old or the new reference in full, never a
    /// reordered/cached-per-CPU partial write — mirrors <c>CachingScheduleResolver.snapshot</c>'s
    /// own precedent (PLAN T119 review F1) for the identical "singleton mutates its own cache
    /// in-place, readers never lock" shape.
    /// </summary>
    volatile CatalogState state;

    ThemeCatalog(CatalogState initialState, (IThemeStore Store, ILogger<ThemeCatalog> Logger)? ownerLoad)
    {
        state = initialState;
        shippedState = initialState;
        this.ownerLoad = ownerLoad;
    }

    /// <summary>One immutable snapshot of a loaded theme set — the slug lookup plus load-order list
    /// <see cref="Load"/> builds from a set of raw sources. A plain reference type (not a
    /// value-tuple) so <see cref="state"/> above can be <c>volatile</c>.</summary>
    sealed record CatalogState(Dictionary<string, ThemeManifest> BySlug, IReadOnlyList<ThemeManifest> Ordered);

    /// <summary>Every loaded theme, in load order. Backed by a dedicated list (not
    /// <c>Dictionary&lt;,&gt;.Values</c>, which guarantees no particular order) because T163 sources
    /// the <c>Station:Theme</c> choice list from this, and T158 iterates it for its AA gate — both
    /// need the order to actually be load order, not an implementation detail of the lookup
    /// dictionary.</summary>
    public IReadOnlyList<ThemeManifest> All => state.Ordered;

    /// <summary>Looks up a theme by its slug. An unresolvable slug returns false — falling back to
    /// the shipped default (SPEC F102.6) is theme RESOLUTION's job (T164), not this lookup's.</summary>
    public bool TryGetBySlug(string slug, [NotNullWhen(true)] out ThemeManifest? theme) =>
        state.BySlug.TryGetValue(slug, out theme);

    /// <summary>
    /// Is <paramref name="slug"/> one of THIS instance's own fixed <see cref="shippedState"/> slugs
    /// (SPEC F103.8, "shipped slugs reserved")? Checked against <see cref="shippedState"/> — never the
    /// current, possibly owner-widened <see cref="state"/> — for the exact reason
    /// <see cref="ReloadOwnerThemesAsync"/>'s own collision check is: an EARLIER owner import must
    /// never block a later, unrelated one from reusing what is, by construction, not actually a
    /// shipped slug. The one place this rule is evaluated (PLAN T184 review F3) — callers that used to
    /// keep their own <see cref="LoadShipped"/> parse purely to answer this question (six embedded-resource
    /// parses per request) now ask the already-loaded DI singleton instead.
    /// </summary>
    public bool IsShippedSlug(string slug) => shippedState.BySlug.ContainsKey(slug);

    /// <summary>
    /// Resolves the active theme for a request (SPEC F102.5/F102.6, STORY-265, PLAN T164). The ONE
    /// seam both <c>GET /spectator/theme.css</c> and <c>GET /api/theme.css</c> call — T160/T161 each
    /// carried their own private copy of this cascade until this task unified it here.
    ///
    /// <para>
    /// Precedence, highest first: <paramref name="cookieSlug"/> → <paramref name="stationSlug"/> →
    /// <see cref="ShippedDefaultSlug"/>. Note what is NOT a rung of this cascade: "settings row" vs
    /// "env default" — that precedence is already decided by the time <paramref name="stationSlug"/>
    /// reaches this method, because the DB-backed settings overlay is registered AFTER env/appsettings
    /// in the configuration pipeline (see <c>StationSettingsHostingExtensions</c>), so a single
    /// <c>IOptionsMonitor&lt;StationOptions&gt;.CurrentValue.Theme</c> read already reflects whichever
    /// one currently wins. This method only ever has ONE station-level value to consider.
    /// </para>
    ///
    /// <para>
    /// <b>An unresolvable slug falls back at EVERY level rather than erroring (SPEC F102.6) — this
    /// method never throws for any input.</b> A cookie naming a slug this catalog doesn't recognise
    /// falls to <paramref name="stationSlug"/>, not straight to the shipped default — a stale cookie
    /// from a theme the operator has since removed must not strand a visitor away from what the
    /// station actually chose (STORY-265 AC10). A <paramref name="stationSlug"/> this catalog doesn't
    /// recognise falls to <see cref="ShippedDefaultSlug"/> (AC9). Both parameters are untrusted,
    /// externally-supplied strings — a request cookie header and an operator-editable setting — and
    /// are used ONLY as a dictionary lookup key here; an arbitrary value can influence which theme is
    /// chosen and nothing else.
    /// </para>
    /// </summary>
    /// <param name="cookieSlug">
    /// The visitor's <see cref="CookieName"/> cookie value, or <see langword="null"/>/empty if absent.
    /// </param>
    /// <param name="stationSlug">
    /// The station's currently effective <c>Station:Theme</c> value, or <see langword="null"/>/empty
    /// if nothing is configured anywhere (settings row, env, or appsettings).
    /// </param>
    public ThemeManifest Resolve(string? cookieSlug, string? stationSlug)
    {
        if (TryResolvePresentSlug(cookieSlug, out var cookieTheme))
            return cookieTheme;

        if (TryResolvePresentSlug(stationSlug, out var stationTheme))
            return stationTheme;

        return TryGetBySlug(ShippedDefaultSlug, out var shipped)
            ? shipped
            : throw new InvalidOperationException(
                $"shipped theme catalog is missing its own default slug '{ShippedDefaultSlug}' — " +
                "this is a boot-time authoring bug (see Program.cs's own startup assertion, which " +
                "should have stopped the process before any request could reach here)");
    }

    /// <summary>A present-but-unresolvable slug and an absent one both mean "try the next rung" —
    /// this is the one place that distinction collapses, so <see cref="Resolve"/>'s cascade reads as
    /// a flat chain of these calls.</summary>
    bool TryResolvePresentSlug(string? slug, [NotNullWhen(true)] out ThemeManifest? theme)
    {
        if (string.IsNullOrEmpty(slug))
        {
            theme = null;
            return false;
        }

        return TryGetBySlug(slug, out theme);
    }

    /// <summary>
    /// Parses and validates a set of raw manifest documents, throwing <see cref="ThemeManifestException"/>
    /// naming the offending theme (and mode/token where applicable) on the first failure found.
    /// Manifests are validated independently by <c>ThemeManifestParser</c>, then checked against one
    /// another for a shared slug (STORY-263 AC7) — a single manifest can never know about its
    /// siblings, so that check belongs here. Builds a fixed, non-reloadable instance — the seam
    /// <see cref="LoadShipped"/> and every fixture-driven test in this codebase use; a
    /// <see cref="CreateForStation"/> instance is the only kind that can later fold owner rows in via
    /// <see cref="ReloadOwnerThemesAsync"/>.
    /// </summary>
    public static ThemeCatalog Load(IEnumerable<ThemeManifestSource> sources) =>
        new(BuildState(sources), ownerLoad: null);

    /// <summary>Loads the theme manifests GenWave ships as embedded resources of this assembly.
    /// Throws if none are found — a boot with zero shipped themes is never a valid outcome to
    /// silently continue with: T164's "fall back to the shipped default" would have no default to
    /// fall back to, so this fails loudly at the same place a bad manifest already does, rather than
    /// producing an empty <see cref="ThemeCatalog"/> that only fails once something asks it for a
    /// theme (review finding, T156).</summary>
    public static ThemeCatalog LoadShipped() => Load(LoadShippedSources());

    /// <summary>
    /// Builds the runtime, DI-registered <see cref="ThemeCatalog"/> (SPEC F103.7, STORY-271, PLAN
    /// T182) — the ONLY construction path that ever calls <paramref name="themeStore"/>, and it never
    /// does so here: this method reads nothing but embedded resources, so registering it in DI can
    /// never itself trigger a connection attempt against <c>ConnectionStrings:Station</c> (mirrors
    /// every Lazy-datasource store's own "resolving is never enough to connect" rule — see
    /// <c>ThemeServiceCollectionExtensions.AddThemeStore</c>'s own remarks). The returned instance
    /// starts serving the shipped-only set immediately — the SPEC F102.7 offline floor holds from the
    /// moment this returns — and gains owner rows only once a caller awaits
    /// <see cref="ReloadOwnerThemesAsync"/> (<c>ThemeCatalogOwnerLoadHostedService</c> does this once
    /// per boot, without blocking host startup; PLAN T184's import route does it again after a write).
    /// </summary>
    public static ThemeCatalog CreateForStation(IThemeStore themeStore, ILogger<ThemeCatalog> logger) =>
        new(BuildState(LoadShippedSources()), (themeStore, logger));

    /// <summary>
    /// Rebuilds the shipped ∪ owner set (SPEC F103.7/F103.8; ARCHITECTURE "Community Catalog v2 →
    /// Data model") through the exact same <see cref="Load"/>/<c>ThemeManifestParser</c> path the
    /// embedded manifests use — every <see cref="OwnerTheme.Definition"/> is wrapped in a
    /// <see cref="ThemeManifestSource"/> and parsed identically, so an owner-imported theme resolves
    /// and composes exactly like a shipped one. This is the one reload hook: called once at boot
    /// (<c>ThemeCatalogOwnerLoadHostedService</c>) and again by a future import (PLAN T184) to pick up
    /// a freshly-stored theme without a process restart.
    ///
    /// <para>
    /// <b>SPEC F102.7 offline floor.</b> Any failure fetching or composing the owner set — an
    /// unreachable/empty database, a malformed stored row, anything — degrades this instance back to
    /// the shipped-only <see cref="shippedState"/> it was constructed with, WARN-logged once, and
    /// never propagates: a station.theme outage must never stop an already-running station from
    /// resolving even its shipped defaults, and must never block whatever caller awaited this (the
    /// boot hosted service's own try/catch is a last-resort guard, not a path this method relies on).
    /// </para>
    ///
    /// <para>
    /// <b>Shipped slugs reserved (SPEC F103.8).</b> An owner row whose slug collides with a shipped
    /// default is skipped — WARN-logged, never admitted — so an import can never shadow the offline
    /// fallback, and <see cref="Load"/>'s own no-duplicate-slug invariant holds by construction rather
    /// than by chance.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This instance was built via <see cref="Load"/>/<see cref="LoadShipped"/>, neither of which
    /// carries a <see cref="IThemeStore"/> to reload from.
    /// </exception>
    public async Task ReloadOwnerThemesAsync(CancellationToken ct)
    {
        if (ownerLoad is not { } load)
            throw new InvalidOperationException(
                $"{nameof(ReloadOwnerThemesAsync)} requires a {nameof(ThemeCatalog)} built via " +
                $"{nameof(CreateForStation)} — {nameof(Load)}/{nameof(LoadShipped)} build a fixed, " +
                "embedded-only snapshot with no station.theme store to reload from");

        try
        {
            var ownerThemes = await load.Store.GetAllAsync(ct);
            if (ownerThemes.Count == 0)
            {
                state = shippedState;
                return;
            }

            var combinedSources = new List<ThemeManifestSource>(LoadShippedSources());
            foreach (var owner in ownerThemes)
            {
                if (IsShippedSlug(owner.Slug))
                {
                    load.Logger.LogWarning(
                        "owner theme '{Slug}' collides with a shipped default's slug and is ignored — " +
                        "shipped slugs are reserved (SPEC F103.8)", owner.Slug);
                    continue;
                }

                combinedSources.Add(new ThemeManifestSource($"station.theme:{owner.Slug}", owner.Definition));
            }

            state = BuildState(combinedSources);
        }
        catch (Exception ex)
        {
            load.Logger.LogWarning(ex,
                "owner theme load failed — falling back to the shipped-only theme set (SPEC F102.7 offline floor)");
            state = shippedState;
        }
    }

    /// <summary>Parses and validates <paramref name="sources"/> into one <see cref="CatalogState"/>,
    /// enforcing the whole-set no-duplicate-slug invariant (STORY-263 AC7) — the one place <see cref="Load"/>
    /// and <see cref="ReloadOwnerThemesAsync"/> share so shipped and owner manifests are never
    /// validated by two different code paths.</summary>
    static CatalogState BuildState(IEnumerable<ThemeManifestSource> sources)
    {
        var bySlug = new Dictionary<string, ThemeManifest>(StringComparer.Ordinal);
        var ordered = new List<ThemeManifest>();
        foreach (var source in sources)
        {
            var theme = ThemeManifestParser.Parse(source);
            if (!bySlug.TryAdd(theme.Slug, theme))
                throw new ThemeManifestException($"duplicate theme slug '{theme.Slug}'");

            ordered.Add(theme);
        }

        return new CatalogState(bySlug, ordered);
    }

    /// <summary>Reads every embedded theme manifest resource of this assembly into raw sources,
    /// without parsing them — <see cref="LoadShipped"/>'s own former body, split out so
    /// <see cref="CreateForStation"/> and <see cref="ReloadOwnerThemesAsync"/> can prepend the same
    /// shipped set ahead of whatever owner rows exist, rather than re-deriving it from an already-built
    /// <see cref="ThemeCatalog"/>.</summary>
    static IReadOnlyList<ThemeManifestSource> LoadShippedSources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var sources = new List<ThemeManifestSource>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains(ShippedResourceSegment, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".json", StringComparison.Ordinal))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new ThemeManifestException($"embedded theme resource '{resourceName}' could not be opened");
            using var reader = new StreamReader(stream);
            sources.Add(new ThemeManifestSource(resourceName, reader.ReadToEnd()));
        }

        if (sources.Count == 0)
            throw new ThemeManifestException(
                $"no shipped theme manifests found — expected embedded resources under '{ShippedResourceSegment}' ending in '.json'");

        return sources;
    }
}
