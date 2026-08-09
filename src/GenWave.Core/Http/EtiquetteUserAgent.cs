namespace GenWave.Core.Http;

using System.Reflection;

/// <summary>
/// Builds the shared "GenWave/&lt;version&gt; (+repo)" etiquette User-Agent every outbound GenWave
/// integration that owes its upstream a descriptive, version-stamped header sends on every request
/// (SPEC F65.1/F76.1/F109.1) — one construction, not several independently hand-copied literals that
/// could silently drift apart (F7 fix, T228 review: <c>MusicBrainzYearLookup</c>'s and
/// <c>HistoryContextProvider</c>'s were verbatim twins of each other, maintained as two separate
/// copies).
/// </summary>
public static class EtiquetteUserAgent
{
    /// <summary>
    /// "GenWave/&lt;version&gt; (+<paramref name="projectUrl"/>)". <paramref name="callingAssembly"/>'s
    /// own build-stamped <see cref="AssemblyInformationalVersionAttribute"/> supplies the version
    /// segment — never a hardcoded literal that silently goes stale — falling back to "unknown" for a
    /// dev build with no stamp at all. Takes the caller's OWN assembly explicitly, rather than
    /// resolving one internally (e.g. via <see cref="Assembly.GetCallingAssembly"/>): each call site's
    /// own build stamp is what identifies THAT integration's shipped version, never this helper's.
    /// </summary>
    public static string Build(Assembly callingAssembly, string projectUrl) =>
        $"GenWave/{callingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"} (+{projectUrl})";
}
