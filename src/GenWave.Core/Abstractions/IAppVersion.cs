namespace GenWave.Core.Abstractions;

/// <summary>
/// The one GenWave release identity for this running process (SPEC F211.1, STORY-483, PLAN T588) —
/// built ONCE at the composition root from the Host assembly's build-stamped
/// <c>AssemblyInformationalVersionAttribute</c> and registered as a DI singleton. Every surface that
/// needs a version string (About, spectator About, the ad worker's re-render marker, outbound
/// User-Agent headers) injects this instead of reflecting on its own assembly — one provider, one
/// parse, one display form everywhere (F211.2). No type outside that provider may read the attribute
/// (enforced as an architecture law, PLAN T590).
/// </summary>
public interface IAppVersion
{
    /// <summary>
    /// The one form shown to a person: <c>"v" + <see cref="Semver"/></c>, e.g. <c>"v5.13.2"</c> — never
    /// build metadata. <c>"unknown"</c> when the build stamp was missing or unparseable.
    /// </summary>
    string Display { get; }

    /// <summary>
    /// The bare SemVer 2.0 core, optionally with a prerelease segment (e.g. <c>"5.13.2"</c>,
    /// <c>"5.13.3-4-gabc1234-dirty"</c>) — no leading <c>v</c>, no build metadata. <c>"unknown"</c> when
    /// the build stamp was missing or unparseable.
    /// </summary>
    string Semver { get; }

    /// <summary>
    /// The full build stamp for debugging only (e.g. <c>"5.13.2+abc1234"</c>) — never shown to a
    /// listener. Carries the raw, unparseable stamp verbatim when the stamp didn't parse as SemVer, or
    /// <c>"unknown"</c> when the stamp was blank or missing entirely.
    /// </summary>
    string Build { get; }
}
