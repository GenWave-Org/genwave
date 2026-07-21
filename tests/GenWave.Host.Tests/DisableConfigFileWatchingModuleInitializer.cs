using System.Runtime.CompilerServices;

namespace GenWave.Host.Tests;

/// <summary>
/// Every spec in this project's <c>Specs/</c> folder builds at least one
/// <c>WebApplicationFactory&lt;Program&gt;</c>, and Program.cs's own <c>WebApplication.CreateBuilder</c>
/// watches <c>appsettings*.json</c> for hot-reload by default (<c>reloadOnChange: true</c>) — each
/// host that does so opens one inotify instance (Linux) for the lifetime of the <c>TestServer</c>.
/// With this suite's ~30+ factory classes now free to build their hosts in TRUE parallel (STORY-196's
/// env-var-race fix removed the collection that used to serialize most of them, which had the side
/// effect of throttling how many hosts were ever open — and therefore watching files — at once), the
/// peak concurrent inotify-instance count can exceed the OS's <c>fs.inotify.max_user_instances</c>
/// (128 by default on Ubuntu, including GitHub Actions runners), surfacing as a flaky
/// <see cref="IOException"/> from <c>FileSystemWatcher.StartRaisingEvents</c> — a new failure mode
/// this fix's own increase in parallelism would otherwise introduce.
///
/// None of these ephemeral, one-shot test hosts need live appsettings.json reload, so this
/// disables it for the whole test process via the well-known <c>hostBuilder:reloadConfigOnChange</c>
/// host-configuration switch, read from the <c>DOTNET_</c>-prefixed environment variable the generic
/// host already honors. Set exactly ONCE here, before any test runs — never toggled or restored — so
/// unlike the per-factory env vars this same fix eliminated, this carries no race: there is nothing
/// for a concurrently-running test to observe a "wrong" value of.
///
/// It also neutralizes AMBIENT config-key leakage (review finding on the UseSetting conversion):
/// <c>UseSetting</c> sits BELOW real environment variables in the app's configuration layering, so a
/// developer shell with e.g. <c>Admin__Password</c> exported (the normal no-Docker
/// <c>dotnet run</c> inner loop) would override every converted factory's injected value and flip
/// ~20 auth-dependent classes to misleading 401s. Nulling the known .NET-binding-form keys once,
/// pre-parallelism, restores the ambient-immunity the old per-factory env overwrites provided —
/// by the same write-once/no-race argument as the reload switch above. The Integration
/// acceptance-gate specs are unaffected: they read the distinct shell-style <c>ADMIN_PASSWORD</c>
/// against a running container, never these binding-form keys.
/// </summary>
static class DisableConfigFileWatchingModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

        Environment.SetEnvironmentVariable("Admin__Password", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Station", null);
        Environment.SetEnvironmentVariable("Llm__Endpoint", null);
        Environment.SetEnvironmentVariable("Llm__Model", null);
        Environment.SetEnvironmentVariable("Station__SpectatorMode", null);
        Environment.SetEnvironmentVariable("Spectator__PublicPort", null);
    }
}
