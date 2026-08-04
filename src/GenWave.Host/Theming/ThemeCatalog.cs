namespace GenWave.Host.Theming;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

/// <summary>
/// Loads and validates the station's theme manifests, then exposes them by slug (STORY-263).
/// Layer A's shipped manifests are embedded resources in this assembly (<see cref="LoadShipped"/>,
/// ARCHITECTURE "Theme system"); once the Layer B editor exists, an owner's stored manifest goes
/// through the exact same <see cref="Load"/> path, because the manifest format makes no
/// distinction between the two (STORY-263 AC4).
///
/// Validation happens once, at load, via <see cref="ThemeManifestParser"/> plus this class's own
/// duplicate-slug check across the whole set. A <see cref="ThemeCatalog"/> therefore either exists
/// fully valid, or does not exist at all — never one that silently drops a bad theme. A malformed
/// shipped manifest is a build-time authoring bug the operator must fix, not a request-time
/// condition to route around, so a failure throws <see cref="ThemeManifestException"/> rather than
/// dropping the offending entry.
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

    readonly Dictionary<string, ThemeManifest> themesBySlug;
    readonly IReadOnlyList<ThemeManifest> orderedThemes;

    ThemeCatalog(Dictionary<string, ThemeManifest> themesBySlug, IReadOnlyList<ThemeManifest> orderedThemes)
    {
        this.themesBySlug = themesBySlug;
        this.orderedThemes = orderedThemes;
    }

    /// <summary>Every loaded theme, in load order. Backed by a dedicated list (not
    /// <c>Dictionary&lt;,&gt;.Values</c>, which guarantees no particular order) because T163 sources
    /// the <c>Station:Theme</c> choice list from this, and T158 iterates it for its AA gate — both
    /// need the order to actually be load order, not an implementation detail of the lookup
    /// dictionary.</summary>
    public IReadOnlyList<ThemeManifest> All => orderedThemes;

    /// <summary>Looks up a theme by its slug. An unresolvable slug returns false — falling back to
    /// the shipped default (SPEC F102.6) is theme RESOLUTION's job (T164), not this lookup's.</summary>
    public bool TryGetBySlug(string slug, [NotNullWhen(true)] out ThemeManifest? theme) =>
        themesBySlug.TryGetValue(slug, out theme);

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
    /// Manifests are validated independently by <see cref="ThemeManifestParser"/>, then checked
    /// against one another for a shared slug (STORY-263 AC7) — a single manifest can never know
    /// about its siblings, so that check belongs here.
    /// </summary>
    public static ThemeCatalog Load(IEnumerable<ThemeManifestSource> sources)
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

        return new ThemeCatalog(bySlug, ordered);
    }

    /// <summary>Loads the theme manifests GenWave ships as embedded resources of this assembly.
    /// Throws if none are found — a boot with zero shipped themes is never a valid outcome to
    /// silently continue with: T164's "fall back to the shipped default" would have no default to
    /// fall back to, so this fails loudly at the same place a bad manifest already does, rather than
    /// producing an empty <see cref="ThemeCatalog"/> that only fails once something asks it for a
    /// theme (review finding, T156).</summary>
    public static ThemeCatalog LoadShipped()
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

        return Load(sources);
    }
}
