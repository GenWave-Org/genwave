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
