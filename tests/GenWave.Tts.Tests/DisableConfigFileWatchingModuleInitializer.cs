using System.Runtime.CompilerServices;

namespace GenWave.Tts.Tests;

/// <summary>
/// <see cref="KokoroStubServer"/>, <see cref="PiperStubServer"/>, and <see cref="MockCompletionsServer"/>
/// each build their own <c>WebApplication.CreateSlimBuilder()</c> host per <c>StartAsync</c> call — and
/// this project's specs call one of those ~75 times across ~17 files (gh-#594). Even the "slim" builder
/// still wires up its default <c>appsettings.json</c>/<c>appsettings.{Environment}.json</c> sources with
/// <c>reloadOnChange: true</c>, so every stub server opens its own inotify-backed
/// <see cref="System.IO.FileSystemWatcher"/> (Linux) for the lifetime of that one test's fake upstream —
/// even though none of these ephemeral, in-memory-configured stubs ever reads from or needs to react to
/// a file on disk. Under xUnit's default cross-class parallelism, many of those hosts are alive at once;
/// the OS's <c>fs.inotify.max_user_instances</c> (128 by default on Ubuntu, including GitHub Actions
/// runners and most dev boxes) is a process-wide ceiling, so it is reachable purely from this project's
/// own footprint on a full <c>dotnet test GenWave.sln</c> run — surfacing as a flaky
/// <see cref="IOException"/> from <c>FileSystemWatcher.StartRaisingEvents</c> in whichever host happened
/// to be the one that tipped the count over.
///
/// Mirrors the house fix already shipped for the same failure mode in
/// <c>GenWave.Host.Tests.DisableConfigFileWatchingModuleInitializer</c>: set the well-known
/// <c>hostBuilder:reloadConfigOnChange</c> host-configuration switch to <c>false</c>, via the
/// <c>DOTNET_</c>-prefixed environment variable the generic host (and <c>CreateSlimBuilder</c> — verified
/// directly, not merely assumed) both honor, exactly ONCE here before any test runs. None of these stub
/// servers' hosts are ever asked to reload config, so nothing observes the value changing — no race with
/// concurrently-running tests.
/// </summary>
static class DisableConfigFileWatchingModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
