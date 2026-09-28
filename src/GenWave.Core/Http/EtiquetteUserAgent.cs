using GenWave.Core.Abstractions;

namespace GenWave.Core.Http;

/// <summary>
/// Builds the shared "GenWave/&lt;version&gt; (+repo)" etiquette User-Agent every outbound GenWave
/// integration that owes its upstream a descriptive, version-stamped header sends on every request
/// (SPEC F65.1/F76.1/F109.1) — one construction, not several independently hand-copied literals that
/// could silently drift apart (F7 fix, T228 review: <c>MusicBrainzYearLookup</c>'s and
/// <c>HistoryContextProvider</c>'s were verbatim twins of each other, maintained as two separate
/// copies). <see cref="ProjectUrl"/> lives here too (PLAN T590 review): it had been re-declared as a
/// private const in every one of the three callers, one more independently-maintained copy of a
/// literal this type already exists to own.
/// </summary>
public static class EtiquetteUserAgent
{
    const string ProjectUrl = "https://github.com/GenWave-Org/genwave";

    /// <summary>
    /// "GenWave/<c>appVersion.Display</c> (+<see cref="ProjectUrl"/>)". <paramref name="appVersion"/>'s
    /// <see cref="IAppVersion.Display"/> is the one provider's display form (e.g. <c>"v5.13.2"</c>) —
    /// taking the provider itself, rather than the already-projected string, keeps every caller's own
    /// read of the version to this one call site (SPEC F211.1, STORY-483, PLAN T590): no type outside
    /// <see cref="GenWave.Core.AppVersion"/> reads
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>, enforced as an
    /// architecture law.
    /// </summary>
    public static string Build(IAppVersion appVersion) => $"GenWave/{appVersion.Display} (+{ProjectUrl})";
}
