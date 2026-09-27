using System.Reflection;
using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;

namespace GenWave.Core;

/// <summary>
/// The one <see cref="IAppVersion"/> implementation and the one parser of a build stamp (SPEC F211.1/
/// F211.2, STORY-483, PLAN T588). <see cref="FromAssembly"/> is the provider the F211.1
/// one-reader law makes the only reader of <see cref="AssemblyInformationalVersionAttribute"/> —
/// the composition root (<c>Program.cs</c>) calls it once, against the Host assembly, and registers
/// the result as a singleton. The one-reader law is enforced by the architecture test
/// <c>Story483_OneVersionLaw.OnlyTheProviderReads</c> (GenWave.Architecture.Tests), which fails if
/// any production type other than this one references <see cref="AssemblyInformationalVersionAttribute"/>
/// or <see cref="System.Diagnostics.FileVersionInfo"/> directly.
///
/// Parse rule: trim, then split on the FIRST <c>+</c>. The left part, minus one leading <c>v</c>/<c>V</c>
/// (never doubled — SPEC F211.2), must be a SemVer 2.0 core with an optional prerelease segment
/// (<c>MAJOR.MINOR.PATCH[-prerelease]</c>) — e.g. <c>5.13.2</c>, <c>0.0.0-dev</c>, or the
/// <c>git describe</c> shape <c>5.13.3-4-gabc1234-dirty</c>. Build metadata is deliberately NOT part of
/// the regex: the <c>+sha</c> suffix already got split off, and SemVer build metadata has no bearing on
/// precedence or display. When it parses, <see cref="Semver"/> is that core (no <c>v</c>),
/// <see cref="Display"/> is <c>"v" + Semver</c>, and <see cref="Build"/> is the full trimmed input minus
/// one leading <c>v</c>. A blank, missing, or unparseable stamp (an untagged dev build, or a bare
/// <c>git describe --always</c> hash with no version at all) yields <c>"unknown"</c> for both
/// <see cref="Display"/> and <see cref="Semver"/>; <see cref="Build"/> keeps the raw stamp for
/// debugging, or <c>"unknown"</c> too when the stamp was blank.
/// </summary>
public sealed partial class AppVersion : IAppVersion
{
    AppVersion(string display, string semver, string build)
    {
        Display = display;
        Semver = semver;
        Build = build;
    }

    /// <inheritdoc/>
    public string Display { get; }

    /// <inheritdoc/>
    public string Semver { get; }

    /// <inheritdoc/>
    public string Build { get; }

    /// <summary>
    /// Reads <paramref name="assembly"/>'s build stamp and parses it — call ONCE, at the composition
    /// root, against the Host assembly (<c>typeof(Program).Assembly</c>), never
    /// <see cref="Assembly.GetEntryAssembly"/>: under <c>WebApplicationFactory</c> the entry assembly
    /// is the test host process, not GenWave.Host, so that overload would silently stamp every
    /// in-process test's version off VSTest's own build instead of the shipped one.
    /// </summary>
    public static AppVersion FromAssembly(Assembly assembly) => From(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The pure parser — exposed directly so it can be exercised without an assembly stamp.</summary>
    public static AppVersion From(string? informationalVersion)
    {
        var raw = (informationalVersion ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new AppVersion("unknown", "unknown", "unknown");

        var plusIndex = raw.IndexOf('+');
        var left = plusIndex >= 0 ? raw[..plusIndex] : raw;
        var core = StripLeadingV(left);

        // Strip only on the success branch: an unparseable stamp keeps its raw form VERBATIM in
        // Build (never re-stripped of a leading v it may not even have doubled).
        return SemVerCore().IsMatch(core)
            ? new AppVersion($"v{core}", core, StripLeadingV(raw))
            : new AppVersion("unknown", "unknown", raw);
    }

    static string StripLeadingV(string value) =>
        value.Length > 0 && (value[0] == 'v' || value[0] == 'V') ? value[1..] : value;

    // SemVer 2.0 MAJOR.MINOR.PATCH with an optional dot-separated prerelease (spec.semver.org §9),
    // deliberately with no build-metadata group of its own — the caller already split that off.
    // ASCII digits only ([0-9], not \d) for an exact SemVer match.
    [GeneratedRegex(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?$")]
    private static partial Regex SemVerCore();
}
