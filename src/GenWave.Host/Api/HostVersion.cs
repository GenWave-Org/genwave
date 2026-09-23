using System.Reflection;

namespace GenWave.Host.Api;

/// <summary>
/// The build-stamped <see cref="AssemblyInformationalVersionAttribute"/> on the Host assembly (SPEC
/// F65.1, STORY-175) — read once at class load since it is fixed for the process's lifetime, so
/// re-reading it per request would only waste reflection. Shared by <see cref="SpectatorController"/>
/// (<c>/spectator/api/about</c>) and <see cref="AboutController"/> (<c>/api/about</c>, SPEC F207.1/
/// F207.2, STORY-474, PLAN T561) so the two surfaces never read the assembly attribute two different
/// ways — one expression, two callers.
/// </summary>
internal static class HostVersion
{
    public static readonly string Value =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
}
