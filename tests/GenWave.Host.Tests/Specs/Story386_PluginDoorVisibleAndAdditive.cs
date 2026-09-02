// STORY-386 — What loaded is visible, and additive by construction (F156.1/.5/.7, F157.2 · PLAN T394)
//
// Every scenario here drives the REAL production composition root (WebApplicationFactory<Program>)
// with a real plugins root on disk — no in-memory PluginLoader shortcut, mirroring StatusApiWebFactory's
// own "deployed pipeline" idiom one file over (Story084_StatusEndpoint.cs). The plugin payload itself is
// examples/genwave-plugin-example's own genuine dotnet build output (Support/ExamplePluginBuildOutput.cs)
// — the SAME reference consumer GenWave.Plugins.Tests' own Story386_ExamplePluginLoadsForReal.cs proves
// the loader itself can load; this file proves the WIRING (T394) around it.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using GenWave.Context;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

// ── Shared web factory ──────────────────────────────────────────────────────
//
// PluginDoorWebFactory/CapturingLoggerProvider moved to tests/GenWave.Host.Tests/Support/ (PLAN T397
// review fold): Story388_AdSpotSourceRegistrationOrder.cs needs the IDENTICAL "real plugin door, real
// WebApplicationFactory<Program>" composition this file's own copies used to provide, and a `file`
// type cannot cross files — see PluginDoorWebFactory's own remarks for the full rationale. Both
// classes are unchanged in shape; only their location (and accessibility, file -> internal) moved.

// ── Shared helpers ──────────────────────────────────────────────────────────

public static class FeaturePluginDoorVisibleAndAdditive
{
    /// <summary>A manifest naming an assembly file that never exists — the cheapest, most direct way
    /// to produce a <see cref="GenWave.Plugins.PluginLoadFailureReason.AssemblyFileMissing"/> skip
    /// without compiling anything (mirrors GenWave.Plugins.Tests' own Roslyn-emitted-plugin facts one
    /// project over, minus the compile step this file has no reason to pay for).</summary>
    static void WriteBrokenPlugin(string pluginsRoot, string slug)
    {
        var directory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;
        File.WriteAllText(Path.Combine(directory, "plugin.json"), """
            {
              "name": "Broken Plugin",
              "version": "0.0.1",
              "assembly": "Missing.dll",
              "entryType": "Broken.EntryPoint",
              "abstractions": "5.6.0"
            }
            """);
    }

    /// <summary>C# source templates <see cref="EmittedHostTestPlugin"/> compiles for the facts that
    /// need a plugin doing something the shipped example never does — mirrors
    /// <c>GenWave.Plugins.Tests.Specs.FeaturePluginLoadsOrSkipsWhole.PluginSource</c>'s own idiom one
    /// project over.</summary>
    static class PluginSource
    {
        /// <summary>T394 review HIGH-2's own regression fixture: a provider whose <c>Key</c> getter
        /// answers <paramref name="firstKey"/> on its FIRST call and <paramref name="secondKey"/> on
        /// every call after — the exact drift shape that froze an UNVALIDATED second answer into an
        /// earlier, buggy <c>KeyMemoizedContextProvider</c> and downed the station. Registers the
        /// provider as the plugin's own sole contract, nothing else.</summary>
        public static string DriftingKeyContextProvider(string firstKey, string secondKey) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace DriftingKeyPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Drifting Key Plugin";

                public void Register(IPluginHost host) => host.AddContextProvider(new Provider());

                sealed class Provider : IContextProvider
                {
                    int readCount;

                    public string Key
                    {
                        get
                        {
                            readCount++;
                            return readCount == 1 ? "{{firstKey}}" : "{{secondKey}}";
                        }
                    }

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) =>
                        Task.FromResult<ContextContent?>(new ContextContent(["drift"], System.DateTimeOffset.MaxValue));
                }
            }
            """;

        /// <summary>The trap-3 (null/blank-key) pin's own fixture: calls <c>host.Setting</c> with a
        /// blank string AND a null string during <c>Register</c>, folding both answers into the
        /// registered provider's own <c>FetchAsync</c> fact — no reflection needed to read them back
        /// (the same "observe through FetchAsync" idiom <c>ScenarioPluginSettingsReadTheirOwnSection</c>'s
        /// own facts already use for the "Sides" setting).</summary>
        public static string BlankKeySettingProbe(string key) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace BlankKeySettingProbe;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Blank Key Setting Probe";

                public void Register(IPluginHost host)
                {
                    var blank = host.Setting("");
                    var absent = host.Setting(null);
                    host.AddContextProvider(new Provider(blank, absent));
                }

                sealed class Provider : IContextProvider
                {
                    readonly string? blank;
                    readonly string? absent;

                    public Provider(string? blank, string? absent)
                    {
                        this.blank = blank;
                        this.absent = absent;
                    }

                    public string Key => "{{key}}";

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) =>
                        Task.FromResult<ContextContent?>(new ContextContent(
                            [$"blank={blank ?? "NULL"};null={absent ?? "NULL"}"], System.DateTimeOffset.MaxValue));
                }
            }
            """;
    }

    static async Task<HttpClient> AuthenticatedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PluginDoorWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static async Task<JsonElement> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production composition (WebApplicationFactory)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPluginProviderJoinsTheFanOut : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;

        public ScenarioAPluginProviderJoinsTheFanOut() => ExamplePluginBuildOutput.CopyInto(root, "example-dice");

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task ThePluginContextProviderResolvesAlongsideWeatherAndHistory()
        {
            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            // The narrowest trigger that forces the host to actually build (SeamCompositionSnapshot's
            // own precedent) — no HTTP round trip needed just to resolve a DI-registered enumerable.
            var providers = factory.Services.GetServices<IContextProvider>().ToList();

            Assert.Contains(providers, p => p.Key == "example-dice");
            Assert.Contains(providers, p => p.Key == "weather");
            Assert.Contains(providers, p => p.Key == "history");
        }
    }

    public sealed class ScenarioADriftingKeyGetterNeverReachesContextPipeline : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task TheValidatedKeyReachesThePipelineAndTheHostBootsCleanly()
        {
            // T394 review HIGH-2's own demanded fact: a plugin whose IContextProvider.Key answers
            // "safe-key" on the loader's own validating read, then "weather" — a REAL built-in's own
            // key — on every read after. An EARLIER, buggy KeyMemoizedContextProvider re-read .Key in
            // its own constructor and froze THAT second, never-validated answer; ContextPipeline's own
            // fail-fast duplicate-key constructor then threw the instant anything resolved it — proven
            // live at review, reproduced exactly here.
            EmittedHostTestPlugin.CreateInto(
                root, "drifting-key-plugin", "Drifting Key Plugin", "DriftingKeyPlugin.EntryPoint",
                PluginSource.DriftingKeyContextProvider("safe-key", "weather"));

            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            // The host boots — resolving ContextPipeline itself is the sharpest proof available: under
            // the bug this fact reproduces, THIS exact call (made for real by ContextTickerService at
            // boot in production) is where the station went down.
            var pipeline = factory.Services.GetRequiredService<ContextPipeline>();
            Assert.NotNull(pipeline);

            // ...and the pipeline's own fan-out saw the VALIDATED key, never the drifted one: exactly
            // one "weather" provider exists (the built-in — the drift never smuggled a SECOND one in),
            // and the plugin's own provider committed under "safe-key", not "weather".
            var providers = factory.Services.GetServices<IContextProvider>().ToList();
            Assert.Contains(providers, p => p.Key == "safe-key");
            Assert.Single(providers, p => p.Key == "weather");
        }
    }

    public sealed class ScenarioStatusReportsEveryOutcome : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;

        public ScenarioStatusReportsEveryOutcome()
        {
            ExamplePluginBuildOutput.CopyInto(root, "example-dice");
            WriteBrokenPlugin(root, "broken-plugin");
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task PluginsArrayCarriesTheLoadedPluginRow()
        {
            await using var factory = new PluginDoorWebFactory(root, enabled: true);
            var client = await AuthenticatedClientAsync(factory);
            var body = await GetStatusAsync(client);

            var plugins = body.GetProperty("plugins").EnumerateArray().ToList();
            var loaded = Assert.Single(plugins, p => p.GetProperty("state").GetString() == "loaded");
            Assert.Equal("Dice Roll Example Plugin", loaded.GetProperty("name").GetString());
            Assert.Equal("1.0.0", loaded.GetProperty("version").GetString());
            Assert.Equal(
                new[] { "IContextProvider" },
                loaded.GetProperty("contracts").EnumerateArray().Select(e => e.GetString()).ToArray());
        }

        [Fact]
        public async Task PluginsArrayCarriesTheSkippedPluginRowWithReason()
        {
            await using var factory = new PluginDoorWebFactory(root, enabled: true);
            var client = await AuthenticatedClientAsync(factory);
            var body = await GetStatusAsync(client);

            var plugins = body.GetProperty("plugins").EnumerateArray().ToList();
            var skipped = Assert.Single(plugins, p => p.GetProperty("state").GetString() == "skipped");
            Assert.True(skipped.TryGetProperty("reason", out var reason));
            Assert.Contains("AssemblyFileMissing", reason.GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task BootWritesOneBoothLogNarrativeRowPerPluginOutcome()
        {
            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            // Forces the host (and therefore the plugin door + its post-Build narration) to run.
            _ = factory.Services;

            Assert.Equal(2, factory.BoothLog.Calls.Count);
            Assert.Contains(factory.BoothLog.Calls, c => c.Kind == "plugin-loaded");
            Assert.Contains(factory.BoothLog.Calls, c => c.Kind == "plugin-skipped");
        }
    }

    public sealed class ScenarioASlugEmbeddingCrLfIsNeutralized : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task ACrLfEmbeddedSlugProducesASingleLineBoothSummaryAndLogMessage()
        {
            // T394 review HIGH-1: PluginLoadReport.Slug reaches the narration's displayName FALLBACK
            // on the COMMON skip path — Name is null for every ManifestUnreadable/ManifestInvalid
            // report (PluginLoader.LoadOne never assigns its own `name` local until AFTER manifest
            // parsing SUCCEEDS, so any parse-time reject leaves it null regardless of what the JSON's
            // own "name" field says). Proven live at review: a directory named with an embedded CR/LF
            // forged a second log line (CWE-117) before this fact's own fix — a manifest missing
            // 'entryType' (a parse-time reject, never reaching the assembly stage) is what puts the
            // slug on that fallback path here. No '/' anywhere in it — a slash would make
            // Directory.CreateDirectory create TWO nested directories instead of one whose own name
            // embeds the newline, silently missing the manifest one level down from where discovery
            // looks (proven empirically while building this fact).
            var slug = "evil-slug\r\nWARN forged plugin-loaded";
            var directory = Directory.CreateDirectory(Path.Combine(root, slug)).FullName;
            File.WriteAllText(Path.Combine(directory, "plugin.json"), """
                {
                  "name": "Whatever",
                  "version": "1.0.0",
                  "assembly": "Plugin.dll",
                  "abstractions": "5.6.0"
                }
                """);

            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            // Forces the host (and therefore the plugin door + its post-Build narration) to run.
            _ = factory.Services;

            var boothRow = Assert.Single(factory.BoothLog.Calls);
            Assert.Equal("plugin-skipped", boothRow.Kind);
            Assert.DoesNotContain('\r', boothRow.Summary);
            Assert.DoesNotContain('\n', boothRow.Summary);

            var logLine = Assert.Single(factory.Logs.Messages);
            Assert.DoesNotContain('\r', logLine);
            Assert.DoesNotContain('\n', logLine);
        }
    }

    public sealed class ScenarioPluginSettingsReadTheirOwnSection : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;

        public ScenarioPluginSettingsReadTheirOwnSection() => ExamplePluginBuildOutput.CopyInto(root, "example-dice");

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task SettingReturnsTheConfiguredValueFromPluginsName()
        {
            // Plugins:example-dice:Sides=2; the example's own DiceRollContextProvider reads it via
            // host.Setting("Sides") (F157.2) and rolls a d2 instead of its d6 default.
            var settings = new Dictionary<string, string> { ["Plugins:example-dice:Sides"] = "2" };
            await using var factory = new PluginDoorWebFactory(root, enabled: true, settings);

            var provider = factory.Services.GetServices<IContextProvider>().Single(p => p.Key == "example-dice");
            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            Assert.Contains(content.Facts, fact => fact.Contains("(a d2)", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SettingReturnsNullForAMissingKey()
        {
            // No Plugins:example-dice:Sides configured at all — host.Setting("Sides") returns null,
            // and the provider's own fail-soft fallback (DiceRollContextProvider.ResolveSides) rolls
            // its DefaultSides d6 instead.
            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            var provider = factory.Services.GetServices<IContextProvider>().Single(p => p.Key == "example-dice");
            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            Assert.Contains(content.Facts, fact => fact.Contains("(a d6)", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SettingReturnsNullForABlankOrNullKeyRatherThanThrowing()
        {
            // T390 r2 review note, pinned at T394 (trap 3): a null/blank key passed to host.Setting
            // must never throw — it resolves the plugin's own bare Plugins:{slug}: segment prefix,
            // which nothing configures a value for directly, so the answer is null exactly like any
            // other unset key. Observed through the registered provider's own FetchAsync fact (the
            // same idiom the two facts above already use), never reflection.
            EmittedHostTestPlugin.CreateInto(
                root, "blank-key-probe", "Blank Key Setting Probe", "BlankKeySettingProbe.EntryPoint",
                PluginSource.BlankKeySettingProbe("blank-key-probe-key"));
            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            var provider = factory.Services.GetServices<IContextProvider>().Single(p => p.Key == "blank-key-probe-key");
            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            Assert.Contains(content.Facts, fact => fact == "blank=NULL;null=NULL");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the closed door
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheClosedDoorIsInert
    {
        [Fact]
        public async Task OneKnobAloneLoadsNothingAndSaysWhichHalfIsMissing()
        {
            var root = Directory.CreateTempSubdirectory("genwave-host-plugin-door-").FullName;
            try
            {
                ExamplePluginBuildOutput.CopyInto(root, "example-dice");

                // A path guaranteed never to exist — never the literal Plugins:Root DEFAULT, so this
                // fact never depends on whatever this machine's own filesystem happens to hold at
                // "/plugins" (STORY-385 AC1's own "root missing = Directory.Exists false" mechanism,
                // proven against a path this test fully controls).
                var neverMounted = Path.Combine(Path.GetTempPath(), $"genwave-plugins-never-mounted-{Guid.NewGuid():n}");

                await using (var enabledOnly = new PluginDoorWebFactory(neverMounted, enabled: true))
                {
                    var status = enabledOnly.Services.GetRequiredService<PluginStatusAccessor>();
                    Assert.NotNull(status.MissingKnobNote);
                    Assert.Contains("mounted", status.MissingKnobNote, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        enabledOnly.Services.GetServices<IContextProvider>(), p => p.Key == "example-dice");
                }

                // The inverse: a real mount, but Plugins:Enabled left unset.
                await using (var mountOnly = new PluginDoorWebFactory(root, enabled: null))
                {
                    var status = mountOnly.Services.GetRequiredService<PluginStatusAccessor>();
                    Assert.NotNull(status.MissingKnobNote);
                    Assert.Contains("Enabled", status.MissingKnobNote, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        mountOnly.Services.GetServices<IContextProvider>(), p => p.Key == "example-dice");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task StatusPluginsIsAnEmptyArrayWhenDisabled()
        {
            await using var factory = new PluginDoorWebFactory(pluginsRoot: null, enabled: null);
            var client = await AuthenticatedClientAsync(factory);
            var body = await GetStatusAsync(client);

            Assert.Empty(body.GetProperty("plugins").EnumerateArray());
        }

        [Fact]
        public async Task TheSeamsCompositionIsByteIdenticalWithTheDoorClosed()
        {
            // The SEAMS generator composition (Development env, no Plugins:Enabled — the SAME
            // door-closed config every other Program.cs boot in this repo's dev/CI defaults to) must
            // register nothing new (SPEC F156.8): the regenerated index matches the checked-in one.
            //
            // Shells out to the generator TOOL itself (mirrors tools/check-seam-index.sh's own
            // mechanism) rather than calling SeamIndexDocument.Generate() directly — that type lives
            // in tools/SeamIndexGenerator, which already references GenWave.Host.Tests for
            // SeamCompositionSnapshot (that project's own csproj comment) — a reference back from here
            // would be circular.
            //
            // Deliberately TWO separate processes — build, then execute — never one combined
            // `dotnet run`: a `dotnet run` invoked as a CHILD of an already-running `dotnet test`
            // process (this fact's own situation) contends with that outer process for the shared
            // MSBuild/Roslyn build-server node pool and can sit for minutes waiting on a node slot
            // — reproduced empirically. `dotnet build` then a plain `dotnet <dll>` execution avoids
            // that contention window entirely: the execution step touches no build server at all.
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            var scratchPath = Path.Combine(Path.GetTempPath(), $"SEAMS-{Guid.NewGuid():n}.md");
            var generatorProject = Path.Combine(repoRoot, "tools", "SeamIndexGenerator", "SeamIndexGenerator.csproj");
            var generatorDll = Path.Combine(repoRoot, "tools", "SeamIndexGenerator", "bin", "Release", "net10.0", "SeamIndexGenerator.dll");

            try
            {
                var build = await RunAsync(
                    repoRoot, "dotnet",
                    ["build", generatorProject, "--configuration", "Release", "-nodeReuse:false"]);
                Assert.True(build.ExitCode == 0, $"Building the SEAMS generator exited {build.ExitCode}: {build.StdErr}\n{build.StdOut}");
                Assert.True(File.Exists(generatorDll), $"Build succeeded but \"{generatorDll}\" was not produced.");

                var run = await RunAsync(repoRoot, "dotnet", [generatorDll, scratchPath]);
                Assert.True(run.ExitCode == 0, $"The SEAMS generator exited {run.ExitCode}: {run.StdErr}\n{run.StdOut}");
                Assert.True(File.Exists(scratchPath), "The SEAMS generator produced no output file.");

                var fresh = File.ReadAllText(scratchPath);
                var committed = File.ReadAllText(Path.Combine(repoRoot, "SEAMS.md"));
                Assert.Equal(committed, fresh);
            }
            finally
            {
                if (File.Exists(scratchPath))
                    File.Delete(scratchPath);
            }
        }

        /// <summary>
        /// Environment variables VSTest's own test-host process sets for ITSELF (coverlet
        /// instrumentation hooks, its own debug/telemetry knobs) that must never leak into a `dotnet`
        /// CLI child THIS fact spawns — reproduced empirically: inheriting <c>DOTNET_STARTUP_HOOKS</c>
        /// (coverlet.collector's own instrumentation hook) into a nested `dotnet build` made that build
        /// hang indefinitely, presumably trying to instrument/report through a collector pipe that only
        /// the OUTER test host's own session actually owns. <c>MSBUILDDISABLENODEREUSE</c>/
        /// <c>DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER</c> are set (not just cleared) for the same
        /// reason one layer down: a nested build must never try to hand its work to a shared
        /// MSBuild/Roslyn server node the OUTER test run's own build already has busy.
        /// </summary>
        static readonly IReadOnlyList<string> EnvironmentVariablesToStrip =
        [
            "DOTNET_STARTUP_HOOKS", "VSTEST_HOST_DEBUG", "VSTEST_RUNNER_DEBUG",
            "TESTINGPLATFORM_TELEMETRY_OPTOUT",
        ];

        /// <summary>
        /// Runs one process to completion, draining stdout/stderr CONCURRENTLY with waiting for exit
        /// (never one after the other — a large enough write on either redirected stream deadlocks a
        /// naive "read this one fully, then wait for exit" ordering the instant the child blocks on
        /// its own full, unread pipe). A 5-minute ceiling kills the whole process tree and fails loudly
        /// rather than hanging a test run forever.
        /// </summary>
        static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
            string workingDirectory, string fileName, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            foreach (var name in EnvironmentVariablesToStrip)
                startInfo.Environment.Remove(name);
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start \"{fileName}\".");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"\"{fileName}\" did not exit within 5 minutes.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
    }
}
