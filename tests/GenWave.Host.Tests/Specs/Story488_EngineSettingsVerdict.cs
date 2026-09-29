// STORY-488 — Status says whether the engine runs the saved settings (gh-#879 · SPEC F213.4–F213.9 · PLAN T600–T602)
//
// BDD specification — xUnit. AC5–AC8/AC11/AC12 were [Fact(Skip = Pending)] at plan time; T602 makes
// them green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/status through WebApplicationFactory with a fake IEngineTuningReader; most
// scenarios drive the probe cycle (EngineSettingsCheck.RunOnceAsync) directly, exactly N ticks per
// scenario — the same "unit-test the cycle, not the timer" shape as DependencyHealthProber/
// ProbeService. The cadence loop itself (EngineSettingsCheck.RunAsync) IS covered directly against a
// FakeTimeProvider (ScenarioProbeLoopCadence), mirroring GenWave.Tts.Tests' own
// Story187_CachedHealthProbes.ScenarioLiveCadence: a PeriodicTimer(TimeProvider) DOES have a
// reliable FakeTimeProvider hook (PLAN T602 review F1 — an earlier draft of this header claimed
// otherwise; DependencyHealthProber.RunAsync already proved it wrong). WARN/INFO read from a
// capturing logger provider. The gw_tuning command is proven against real Liquidsoap (engine image)
// — Integration.

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Host.Engine;
using GenWave.Host.Health;
using GenWave.Host.Options;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSettingsVerdict
{
    /// <summary>Logs in against <paramref name="factory"/>'s own <see cref="EngineSettingsWebFactory.Password"/>
    /// and hands back a cookie-carrying client — shared by every AC5–AC11 scenario below (mirrors
    /// Story163_NamedAuthorizationPolicies's own <c>LoggedInClientAsync</c>).</summary>
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = EngineSettingsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public sealed class ScenarioTheEngineReportsWhatItRuns
    {
        // Given: engine image started with GW_XFADE_MIN=2 GW_XFADE_MAX=8 GW_SAFE_GAP_SECONDS=7; `gw_tuning` on :1234

        static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);

        static string EngineScriptText => File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

        /// <summary>
        /// AC1 — the reply carries all three keys with the started values. Builds the real engine
        /// image from engine/ (Dockerfile + entrypoint.sh, unmodified), boots it with the AC1 env
        /// values and an API_HOST that resolves nowhere, waits for :1234, sends the real
        /// <c>gw_tuning</c> telnet command, and asserts the literal reply.
        /// <para>
        /// API_HOST=nohost.invalid fails DNS on every entrypoint.sh attempt, but T599's 30 s
        /// wall-clock retry budget (SPEC F213.2) has no env override to shorten it — PLAN T600
        /// sanctions paying that wait inside this fact rather than dodging it, so that is the
        /// choice made here.
        /// </para>
        /// </summary>
        [Fact, Trait("Category", "Integration")]
        public async Task RepliesWithTheStartedValues()
        {
            const string image = "gw-engine-spec:t600";
            var container = $"gw-t600-{Guid.NewGuid():N}";
            DockerCli.Arrange("build", "-q", "-t", image, Path.Combine(RepoRoot, "engine"));
            try
            {
                DockerCli.Arrange("run", "-d", "--rm", "--name", container,
                    "-e", "GW_XFADE_MIN=2",
                    "-e", "GW_XFADE_MAX=8",
                    "-e", "GW_SAFE_GAP_SECONDS=7",
                    "-e", "API_HOST=nohost.invalid",
                    "-e", "ICECAST_SOURCE_PASSWORD=spec",
                    "-v", $"{Path.Combine(RepoRoot, "engine", "genwave.liq")}:/genwave.liq:ro",
                    "-p", "127.0.0.1::1234",
                    image);

                var port = HostPort(container);
                // ~30 s entrypoint budget + Liquidsoap boot; 90 s leaves generous headroom.
                var reply = await SendCommandAsync(port, "gw_tuning", TimeSpan.FromSeconds(90));

                Assert.Equal(
                    "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0",
                    reply);
            }
            finally
            {
                DockerCli.Run("rm", "-f", container);
            }
        }

        /// <summary>AC2 — gw_tuning is the only new server command in genwave.liq</summary>
        [Fact]
        public void OnlyOneRegisteredCommand()
        {
            Assert.Equal(1, CountOccurrences(EngineScriptText, "server.register("));
        }

        /// <summary>AC2 — the one registered command is gw_tuning</summary>
        [Fact]
        public void TheRegisteredCommandIsGwTuning()
        {
            Assert.Contains("\"gw_tuning\",", EngineScriptText, StringComparison.Ordinal);
        }

        /// <summary>AC2 — its handler ignores its argument (sets nothing)</summary>
        [Fact]
        public void TheCommandIsReadOnly()
        {
            Assert.Contains("fun (_) -> \"GW_XFADE_MIN=", EngineScriptText, StringComparison.Ordinal);
        }

        static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        // --- docker orchestration: build the real engine image, boot it, talk telnet ---

        /// <summary>Reads back the ephemeral host port <c>-p 127.0.0.1::1234</c> published.</summary>
        static int HostPort(string container)
        {
            var r = DockerCli.Run("port", container, "1234");
            if (r.ExitCode != 0)
                throw new InvalidOperationException($"docker port {container} 1234 failed: {r.Stderr}");

            // e.g. "127.0.0.1:54321" — take the first published line's port.
            var line = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
            var colon = line.LastIndexOf(':');
            return int.Parse(line[(colon + 1)..]);
        }

        /// <summary>
        /// Connects to the real telnet control socket (retrying until <paramref name="budget"/>
        /// elapses, since the engine takes ~30 s to boot under T599's api-config wait), sends
        /// <paramref name="command"/>, and returns the reply up to the Liquidsoap-protocol
        /// <c>END</c> sentinel — the same transport shape as production's LiquidsoapControl.
        /// <para>
        /// Docker's published-port proxy accepts the TCP handshake the instant the container
        /// starts — well before Liquidsoap is listening inside it — so an early connect can
        /// "succeed" yet never yield a real reply. A read that never sees <c>END</c> is therefore
        /// treated as a retryable miss (close, wait, reconnect), not a final empty answer.
        /// </para>
        /// </summary>
        static async Task<string> SendCommandAsync(int port, string command, TimeSpan budget)
        {
            var deadline = DateTime.UtcNow + budget;
            Exception? last = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var client = new TcpClient();
                    var connect = client.ConnectAsync(IPAddress.Loopback, port);
                    if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(2))) != connect)
                        throw new TimeoutException("connect timed out");
                    await connect;

                    await using var stream = client.GetStream();
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"));

                    var sb = new StringBuilder();
                    var buf = new byte[4096];
                    var foundEnd = false;
                    var readDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                    while (DateTime.UtcNow < readDeadline)
                    {
                        var readTask = stream.ReadAsync(buf).AsTask();
                        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2))) != readTask)
                            break;
                        var read = await readTask;
                        if (read <= 0) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, read));
                        if (sb.ToString().Replace("\r", "").Split('\n').Contains("END")) { foundEnd = true; break; }
                    }

                    if (foundEnd)
                    {
                        var lines = sb.ToString().Replace("\r", "").Split('\n');
                        return string.Join('\n', lines.TakeWhile(l => l != "END")).Trim();
                    }

                    last = new TimeoutException("connected but no END sentinel yet (Liquidsoap not listening behind the proxy yet)");
                }
                catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
                {
                    last = ex;
                }

                await Task.Delay(500);
            }
            throw new TimeoutException($"gw_tuning: port {port} never answered within {budget}.", last);
        }
    }

    public sealed class ScenarioEqualValues
    {
        // Given: effective settings 2 / 8 / 7.0; reader returns "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0"
        readonly EngineSettingsVerdict verdict = EngineSettingsVerdict.Compute(
            new Dictionary<string, string?>
            {
                ["GW_XFADE_MIN"] = "2",
                ["GW_XFADE_MAX"] = "8",
                ["GW_SAFE_GAP_SECONDS"] = "7.0",
            },
            "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");

        /// <summary>AC3 — verdict is inSync</summary>
        [Fact]
        public void InSync() => Assert.Equal(EngineSettingsState.InSync, verdict.State);

        /// <summary>AC3 — differs is empty</summary>
        [Fact]
        public void NothingDiffers() => Assert.Empty(verdict.Differs);
    }

    public sealed class ScenarioOneValueDiffers
    {
        // Given: effective GW_XFADE_MIN 3 (others equal); reader reports 2.0
        readonly EngineSettingsVerdict verdict = EngineSettingsVerdict.Compute(
            new Dictionary<string, string?>
            {
                ["GW_XFADE_MIN"] = "3",
                ["GW_XFADE_MAX"] = "8",
                ["GW_SAFE_GAP_SECONDS"] = "7.0",
            },
            "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");

        /// <summary>AC4 — verdict is restartNeeded</summary>
        [Fact]
        public void RestartNeeded() => Assert.Equal(EngineSettingsState.RestartNeeded, verdict.State);

        /// <summary>AC4 — differs is exactly ["GW_XFADE_MIN"]</summary>
        [Fact]
        public void NamesTheKey() => Assert.Equal(["GW_XFADE_MIN"], verdict.Differs);
    }

    public sealed class ScenarioEffectiveValueOutprecisesTheEngine
    {
        // Given: effective GW_XFADE_MIN "2.1234567890123" (others at defaults); the engine echoes
        // it back rounded to its own 12-significant-digit print precision (PLAN T601 review F1 —
        // an exact == here would read a saved value as restartNeeded forever).
        readonly EngineSettingsVerdict verdict = EngineSettingsVerdict.Compute(
            new Dictionary<string, string?>
            {
                ["GW_XFADE_MIN"] = "2.1234567890123",
                ["GW_XFADE_MAX"] = "8",
                ["GW_SAFE_GAP_SECONDS"] = "7",
            },
            "GW_XFADE_MIN=2.12345678901 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");

        /// <summary>AC9/F213.6 — rounding to the engine's print precision before comparing gives inSync.</summary>
        [Fact]
        public void InSync() => Assert.Equal(EngineSettingsState.InSync, verdict.State);
    }

    public sealed class ScenarioGenuineDifferenceSurvivesTheRounding
    {
        // Given: effective GW_XFADE_MIN 2.12345678902 (12 significant digits); reader reports
        // 2.12345678901 — a real difference at the engine's own print precision, which the
        // rounding fix must not swallow (PLAN T601 review F1).
        readonly EngineSettingsVerdict verdict = EngineSettingsVerdict.Compute(
            new Dictionary<string, string?>
            {
                ["GW_XFADE_MIN"] = "2.12345678902",
                ["GW_XFADE_MAX"] = "8",
                ["GW_SAFE_GAP_SECONDS"] = "7",
            },
            "GW_XFADE_MIN=2.12345678901 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");

        /// <summary>A true 12th-significant-digit difference still gives restartNeeded.</summary>
        [Fact]
        public void RestartNeeded() => Assert.Equal(EngineSettingsState.RestartNeeded, verdict.State);
    }

    public sealed class ScenarioTelnetReaderAgainstAFakeSocket : IAsyncDisposable
    {
        // Given: a loopback FakeEngineServer that answers gw_tuning with the started values
        // (PLAN T601 — "telnet impl covered against a fake socket").
        const string Reply = "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0";

        readonly FakeEngineServer engineServer = new(_ => Reply);
        readonly LiquidsoapTuningReader reader;

        public ScenarioTelnetReaderAgainstAFakeSocket()
        {
            reader = new LiquidsoapTuningReader(new LiquidsoapOptions
            {
                Host = "127.0.0.1",
                Port = engineServer.Port,
            });
        }

        /// <summary>Sends exactly the gw_tuning command — no other telnet traffic.</summary>
        [Fact]
        public async Task SendsGwTuning()
        {
            await reader.ReadAsync(CancellationToken.None);

            Assert.Equal(["gw_tuning"], engineServer.Commands);
        }

        /// <summary>Returns the reply line verbatim, unparsed.</summary>
        [Fact]
        public async Task ReturnsTheReplyLineVerbatim()
        {
            Assert.Equal(Reply, await reader.ReadAsync(CancellationToken.None));
        }

        public async ValueTask DisposeAsync() => await engineServer.DisposeAsync();
    }

    public sealed class ScenarioTelnetReaderTimesOut : IAsyncDisposable
    {
        // Given: a listener that accepts the connection but never replies; reader given a 200 ms
        // timeout (PLAN T601 review F2 — proves the "or times out" half of AC9, not just "throws").
        readonly NeverRepliesEngineServer engineServer = new();
        readonly LiquidsoapTuningReader reader;

        public ScenarioTelnetReaderTimesOut()
        {
            reader = new LiquidsoapTuningReader(
                new LiquidsoapOptions { Host = "127.0.0.1", Port = engineServer.Port },
                TimeSpan.FromMilliseconds(200));
        }

        /// <summary>AC9 — the reader itself throws TimeoutException, never a bare OperationCanceledException.</summary>
        [Fact]
        public async Task ReadAsyncThrowsTimeoutException() =>
            await Assert.ThrowsAsync<TimeoutException>(() => reader.ReadAsync(CancellationToken.None));

        /// <summary>AC9 — ComputeAsync maps that timeout to Unknown, exactly like a refused connection.</summary>
        [Fact]
        public async Task ComputeAsyncGivesUnknown()
        {
            var verdict = await EngineSettingsVerdict.ComputeAsync(
                reader,
                new Dictionary<string, string?>
                {
                    ["GW_XFADE_MIN"] = "2",
                    ["GW_XFADE_MAX"] = "8",
                    ["GW_SAFE_GAP_SECONDS"] = "7",
                },
                CancellationToken.None);

            Assert.Equal(EngineSettingsState.Unknown, verdict.State);
        }

        public async ValueTask DisposeAsync() => await engineServer.DisposeAsync();
    }

    public sealed class ScenarioStatusCarriesTheVerdict : IAsyncLifetime
    {
        // Given: WAF at its default effective config (GW_XFADE_MIN 2, GW_XFADE_MAX 8,
        // GW_SAFE_GAP_SECONDS 7 — appsettings.json's own defaults); fake reader reports GW_XFADE_MIN
        // 3.0 (a genuine difference on that one key alone); one probe tick; GET /api/status.
        JsonElement engineJson;

        public async Task InitializeAsync()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=3.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            await using var factory = new EngineSettingsWebFactory(reader, new CapturingLevelLoggerProvider());
            var check = factory.Services.GetRequiredService<EngineSettingsCheck>();
            await check.RunOnceAsync(CancellationToken.None);

            var client = await LoggedInClientAsync(factory);
            var response = await client.GetAsync("/api/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            engineJson = body.GetProperty("engine");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC5 — engine.settings is "restartNeeded"</summary>
        [Fact]
        public void Settings() =>
            Assert.Equal("restartNeeded", engineJson.GetProperty("settings").GetString());

        /// <summary>AC5 — engine.differs is ["GW_XFADE_MIN"]</summary>
        [Fact]
        public void Differs() =>
            Assert.Equal(
                ["GW_XFADE_MIN"],
                engineJson.GetProperty("differs").EnumerateArray().Select(e => e.GetString() ?? "").ToArray());

        /// <summary>AC12 — the engine block holds only settings and differs</summary>
        [Fact]
        public void NoValuesLeak() =>
            Assert.Equal(["settings", "differs"], engineJson.EnumerateObject().Select(p => p.Name).ToArray());
    }

    public sealed class ScenarioStatusNeverOpensASocket : IAsyncLifetime
    {
        // Given: WAF; counting fake reader; one probe tick; 5 × GET /api/status inside the interval —
        // the reader's own call count must not grow across them (AC6).
        int callCountAfterProbe;
        int callCountAfterGets;

        public async Task InitializeAsync()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            await using var factory = new EngineSettingsWebFactory(reader, new CapturingLevelLoggerProvider());
            var check = factory.Services.GetRequiredService<EngineSettingsCheck>();
            await check.RunOnceAsync(CancellationToken.None);
            callCountAfterProbe = reader.CallCount;

            var client = await LoggedInClientAsync(factory);
            for (var i = 0; i < 5; i++)
            {
                var response = await client.GetAsync("/api/status");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            callCountAfterGets = reader.CallCount;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC6 — the reader count stays exactly what it was after the one probe tick; GET
        /// /api/status never grows it (never opens a socket of its own).</summary>
        [Fact]
        public void ReaderNotCalled() => Assert.Equal(callCountAfterProbe, callCountAfterGets);
    }

    public sealed class ScenarioEnteringRestartNeeded : IAsyncLifetime
    {
        // Given: WAF; fake reader scripted inSync → restartNeeded → restartNeeded; 3 probe ticks.
        IReadOnlyList<string> warnMessages = [];

        public async Task InitializeAsync()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0",
                "GW_XFADE_MIN=3.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0",
                "GW_XFADE_MIN=3.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            var logs = new CapturingLevelLoggerProvider();
            await using var factory = new EngineSettingsWebFactory(reader, logs);
            var check = factory.Services.GetRequiredService<EngineSettingsCheck>();

            for (var i = 0; i < 3; i++)
                await check.RunOnceAsync(CancellationToken.None);

            warnMessages = logs.Entries
                .Where(e => e.Level == LogLevel.Warning
                    && e.Category == typeof(EngineSettingsCheck).FullName)
                .Select(e => e.Message)
                .ToArray();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC7 — exactly one WARN across all 3 ticks (entering RestartNeeded once; staying
        /// there on the 3rd tick does not re-warn — PLAN T602's own edge-case decision).</summary>
        [Fact]
        public void OneWarn() => Assert.Single(warnMessages);

        /// <summary>AC7 — the WARN names GW_XFADE_MIN</summary>
        [Fact]
        public void WarnNamesTheKey() =>
            Assert.Contains(warnMessages, m => m.Contains("GW_XFADE_MIN", StringComparison.Ordinal));

        /// <summary>AC7 — the WARN names `docker compose restart engine`</summary>
        [Fact]
        public void WarnNamesTheFix() =>
            Assert.Contains(warnMessages, m => m.Contains("docker compose restart engine", StringComparison.Ordinal));
    }

    public sealed class ScenarioBackInSync : IAsyncLifetime
    {
        // Given: WAF; fake reader scripted restartNeeded → inSync; 2 probe ticks.
        IReadOnlyList<string> infoMessages = [];

        public async Task InitializeAsync()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=3.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0",
                "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            var logs = new CapturingLevelLoggerProvider();
            await using var factory = new EngineSettingsWebFactory(reader, logs);
            var check = factory.Services.GetRequiredService<EngineSettingsCheck>();

            await check.RunOnceAsync(CancellationToken.None);
            await check.RunOnceAsync(CancellationToken.None);

            infoMessages = logs.Entries
                .Where(e => e.Level == LogLevel.Information
                    && e.Category == typeof(EngineSettingsCheck).FullName)
                .Select(e => e.Message)
                .ToArray();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC8 — exactly one INFO saying the engine settings are back in sync</summary>
        [Fact]
        public void OneInfo() => Assert.Single(infoMessages);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineUnreachable
    {
        // Given: reader throws (socket refused / timeout)
        static readonly Dictionary<string, string?> EffectiveConfig = new()
        {
            ["GW_XFADE_MIN"] = "2",
            ["GW_XFADE_MAX"] = "8",
            ["GW_SAFE_GAP_SECONDS"] = "7",
        };

        /// <summary>AC9 — verdict is unknown</summary>
        [Fact]
        public async Task Unknown()
        {
            var reader = new FakeThrowingEngineTuningReader(new SocketException());

            var verdict = await EngineSettingsVerdict.ComputeAsync(reader, EffectiveConfig, CancellationToken.None);

            Assert.Equal(EngineSettingsState.Unknown, verdict.State);
        }

        /// <summary>AC9 — no new WARN (via WAF + one probe tick)</summary>
        [Fact]
        public async Task NoNewWarn()
        {
            var reader = new FakeThrowingEngineTuningReader(new SocketException());
            var logs = new CapturingLevelLoggerProvider();
            await using var factory = new EngineSettingsWebFactory(reader, logs);
            var check = factory.Services.GetRequiredService<EngineSettingsCheck>();

            await check.RunOnceAsync(CancellationToken.None);

            Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning);
        }
    }

    public sealed class ScenarioGarbledReply
    {
        // Given: reader returns "GW_XFADE_MIN=abc"
        static readonly Dictionary<string, string?> EffectiveConfig = new()
        {
            ["GW_XFADE_MIN"] = "2",
            ["GW_XFADE_MAX"] = "8",
            ["GW_SAFE_GAP_SECONDS"] = "7",
        };

        /// <summary>AC10 — verdict is unknown</summary>
        [Fact]
        public void Unknown()
        {
            var verdict = EngineSettingsVerdict.Compute(EffectiveConfig, "GW_XFADE_MIN=abc");

            Assert.Equal(EngineSettingsState.Unknown, verdict.State);
        }
    }

    public sealed class ScenarioBeforeTheFirstProbe : IAsyncLifetime
    {
        // Given: WAF just booted; no probe tick; GET /api/status
        HttpStatusCode statusCode;
        JsonElement engineJson;

        public async Task InitializeAsync()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            await using var factory = new EngineSettingsWebFactory(reader, new CapturingLevelLoggerProvider());

            var client = await LoggedInClientAsync(factory);
            var response = await client.GetAsync("/api/status");
            statusCode = response.StatusCode;
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            engineJson = body.GetProperty("engine");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC11 — engine.settings is "unknown"</summary>
        [Fact]
        public void Unknown() => Assert.Equal("unknown", engineJson.GetProperty("settings").GetString());

        /// <summary>AC11 — status returns 200</summary>
        [Fact]
        public void StatusIs200() => Assert.Equal(HttpStatusCode.OK, statusCode);
    }

    // ---------------------------------------------------------------------
    // PLAN T602 REVIEW F1 — the cadence loop itself, not just RunOnceAsync
    // ---------------------------------------------------------------------

    public sealed class ScenarioProbeLoopCadence
    {
        // Given: EngineSettingsCheck.RunAsync driven by a FakeTimeProvider — mirrors
        // GenWave.Tts.Tests' Story187_CachedHealthProbes.ScenarioLiveCadence one project over,
        // proving PeriodicTimer(TimeProvider) DOES have a reliable FakeTimeProvider hook. No
        // wall-clock sleep in this scenario ever waits OUT the production interval: cycles elapse
        // only on Time.Advance. Each fact below gets its own fresh arrange (own check/reader/time),
        // since the facts assert different call counts at different points in the loop's life.
        static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        static (EngineSettingsCheck Check, ScriptedEngineTuningReader Reader, FakeTimeProvider Time) Arrange()
        {
            var reader = new ScriptedEngineTuningReader(
                "GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0");
            var time = new FakeTimeProvider();
            var check = new EngineSettingsCheck(
                reader,
                new ConfigurationBuilder().Build(),
                new EngineSettingsStatus(),
                time,
                NullLogger<EngineSettingsCheck>.Instance);
            return (check, reader, time);
        }

        /// <summary>AC11 — the first cycle runs at boot, with no Advance at all: the reader is
        /// called exactly once (a verdict exists as soon as possible, not only after the first
        /// full interval elapses).</summary>
        [Fact]
        public async Task FirstCycleRunsImmediatelyWithNoAdvance()
        {
            var (check, reader, _) = Arrange();
            using var cts = new CancellationTokenSource();
            var runTask = check.RunAsync(() => Interval, cts.Token);

            await WaitUntilAsync(() => reader.CallCount >= 1);

            Assert.Equal(1, reader.CallCount);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        }

        /// <summary>Before the interval elapses, no second cycle runs.</summary>
        [Fact]
        public async Task NoSecondCycleBeforeTheIntervalElapses()
        {
            var (check, reader, _) = Arrange();
            using var cts = new CancellationTokenSource();
            var runTask = check.RunAsync(() => Interval, cts.Token);
            await WaitUntilAsync(() => reader.CallCount >= 1);

            // A bounded real-time settle window, NOT a wait for the production interval: nothing
            // but an explicit Advance below can ever start a second cycle, so the count staying at
            // 1 here is deterministic regardless of how slow the runner is — it can only make this
            // fact slower, never wrong.
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, reader.CallCount);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        }

        /// <summary>After the interval elapses, exactly one more cycle runs.</summary>
        [Fact]
        public async Task OneMoreCycleAfterTheIntervalElapses()
        {
            var (check, reader, time) = Arrange();
            using var cts = new CancellationTokenSource();
            var runTask = check.RunAsync(() => Interval, cts.Token);
            await WaitUntilAsync(() => reader.CallCount >= 1);

            time.Advance(Interval);
            await WaitUntilAsync(() => reader.CallCount >= 2);

            Assert.Equal(2, reader.CallCount);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        }

        /// <summary>Cancelling the token ends RunAsync: the task completes.</summary>
        [Fact]
        public async Task CancellingTheTokenEndsTheLoop()
        {
            var (check, reader, _) = Arrange();
            using var cts = new CancellationTokenSource();
            var runTask = check.RunAsync(() => Interval, cts.Token);
            await WaitUntilAsync(() => reader.CallCount >= 1);

            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        }

        /// <summary>Polls <paramref name="condition"/> on a short real-time cadence until it holds,
        /// or throws — the same "give the loop's continuations a real-time window to settle"
        /// shape as Story187's own WaitUntil, used here only to synchronize with a FakeTimeProvider
        /// tick, never to wait out a production interval.</summary>
        static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            if (!condition())
            {
                throw new TimeoutException("condition was never satisfied");
            }
        }
    }

    // ---------------------------------------------------------------------
    // PLAN T602 REVIEW F1 — the DI graph resolves
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineSettingsCheckServiceIsRegistered
    {
        /// <summary>
        /// Proves the DI graph <c>AddGenWaveDependencyHealth</c> wires (PLAN T602) actually builds.
        /// GenWave.Architecture.Tests has no DI-graph-validation spec (see
        /// tests/GenWave.Architecture.Tests/Support/ProductionArchitecture.cs — its checks are
        /// namespace/reference rules, not a container build), and every WAF elsewhere in THIS file
        /// strips <see cref="IHostedService"/> before it ever gets a chance to construct
        /// <see cref="EngineSettingsCheckService"/> — so nothing else in the suite proves the
        /// registration itself resolves.
        /// <para>
        /// Built off a plain <see cref="ServiceProvider"/>, not a <see cref="WebApplicationFactory{TEntryPoint}"/>:
        /// <see cref="ServiceCollection.BuildServiceProvider(bool)"/> only ever CONSTRUCTS the
        /// registered services, never calls <see cref="IHostedService.StartAsync"/> — the same
        /// distinction that keeps this cheap and safe. Only the four dependencies
        /// <c>AddGenWaveDependencyHealth</c>'s own graph actually needs are fed in (TimeProvider,
        /// IConfiguration, DependencyHealthStore, IEngineTuningReader) — none of them touch Postgres
        /// or Liquidsoap, so no fake stands in for a real one.
        /// </para>
        /// </summary>
        [Fact]
        public void ResolvesFromARealServiceProvider()
        {
            var configuration = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<DependencyHealthStore>();
            services.AddSingleton<IEngineTuningReader>(
                new ScriptedEngineTuningReader("GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0"));
            services.AddGenWaveDependencyHealth(configuration);

            using var provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Contains(provider.GetServices<IHostedService>(), service => service is EngineSettingsCheckService);
        }
    }
}

/// <summary>An <see cref="IEngineTuningReader"/> that always fails — stands in for a refused
/// connection or a timed-out read (AC9).</summary>
file sealed class FakeThrowingEngineTuningReader(Exception toThrow) : IEngineTuningReader
{
    public Task<string> ReadAsync(CancellationToken ct) => Task.FromException<string>(toThrow);
}

/// <summary>
/// A loopback listener that takes the TCP handshake and then never writes a single byte back —
/// stands in for a Liquidsoap socket that accepted the connection but hung (PLAN T601 review F2,
/// the "or times out" half of AC9), which <see cref="FakeEngineServer"/> can't produce since its
/// <c>respond</c> callback always answers.
/// <para>
/// Not <c>file</c>-scoped, unlike <see cref="FakeThrowingEngineTuningReader"/>: it's held in a
/// typed field (part of a member signature), and a file-local type can only appear there when its
/// own enclosing type is file-local too — <see cref="FakeThrowingEngineTuningReader"/> instead only
/// ever appears behind a <c>var</c> local, which isn't a signature.
/// </para>
/// </summary>
sealed class NeverRepliesEngineServer : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource cts = new();
    readonly Task acceptLoop;

    public NeverRepliesEngineServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        acceptLoop = AcceptForeverAsync(cts.Token);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    async Task AcceptForeverAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                // Hold the connection open — silence, not a reply — until torn down.
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (ObjectDisposedException) { /* expected on shutdown */ }
    }

    public async ValueTask DisposeAsync()
    {
        await cts.CancelAsync();
        listener.Stop();
        try { await acceptLoop; } catch { /* expected on shutdown */ }
        cts.Dispose();
    }
}

/// <summary>
/// An <see cref="IEngineTuningReader"/> that replays a scripted sequence of gw_tuning replies — one
/// per <see cref="EngineSettingsCheck.RunOnceAsync"/> call; once the script runs out, the last reply
/// repeats, so a scenario can tick past its scripted transitions without throwing. <see cref="CallCount"/>
/// is what AC6 checks never grows across a run of <c>GET /api/status</c> calls, and what
/// <c>ScenarioProbeLoopCadence</c> counts probe cycles with.
/// <para>
/// Not <c>file</c>-scoped, unlike <see cref="FakeThrowingEngineTuningReader"/>: <c>ScenarioProbeLoopCadence</c>'s
/// own <c>Arrange</c> helper returns it inside a tuple, which is a member signature — a file-local
/// type can only appear there when its own enclosing type is file-local too (the same rule
/// <see cref="NeverRepliesEngineServer"/>'s own remarks already document).
/// </para>
/// </summary>
sealed class ScriptedEngineTuningReader(params string[] replies) : IEngineTuningReader
{
    int callCount;

    public int CallCount => callCount;

    public Task<string> ReadAsync(CancellationToken ct)
    {
        var index = Interlocked.Increment(ref callCount) - 1;
        return Task.FromResult(replies[Math.Min(index, replies.Length - 1)]);
    }
}

/// <summary>
/// Captures every log entry at every level, unlike Story186's own <c>CapturingDebugLoggerProvider</c>
/// (Debug+ only) — AC7/AC8 need the WARN/INFO transition lines by exact level, and AC7's "exactly
/// one WARN across 3 ticks" needs nothing filtered away before it reaches <see cref="Entries"/>.
/// </summary>
file sealed class CapturingLevelLoggerProvider : ILoggerProvider
{
    readonly List<(LogLevel Level, string Category, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries
    {
        get { lock (entries) return entries.ToList(); }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    void Add(LogLevel level, string category, string message) { lock (entries) entries.Add((level, category, message)); }

    sealed class Logger(CapturingLevelLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(logLevel, category, formatter(state, exception));
        }
    }
}

/// <summary>
/// Boots a real Host pipeline for the AC5–AC12 <c>GET /api/status</c> scenarios: replaces the same 4
/// Postgres-backed dependencies Story084's <c>StatusApiWebFactory</c> fakes (StatusController's own
/// deps), plus <see cref="IEngineTuningReader"/> itself so each scenario scripts its own gw_tuning
/// replies, strips every <see cref="IHostedService"/> (these scenarios drive
/// <see cref="EngineSettingsCheck.RunOnceAsync"/> once per tick; the loop itself is covered by
/// <c>ScenarioProbeLoopCadence</c>), and wires
/// <paramref name="logs"/> at Trace for <see cref="EngineSettingsCheck"/>'s own category so its
/// WARN/INFO transition lines are observable.
/// </summary>
file sealed class EngineSettingsWebFactory(IEngineTuningReader reader, CapturingLevelLoggerProvider logs)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x6f3";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("GenWave.Host.Engine.EngineSettingsCheck", LogLevel.Trace);
            logging.AddProvider(logs);
        });

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap or DB connections during this test — same reasoning as Story084's
            // StatusApiWebFactory for the first 4 fakes below.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));

            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());

            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());

            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // The one dependency unique to this suite: each scenario scripts its own gw_tuning replies.
            services.RemoveAll<IEngineTuningReader>();
            services.AddSingleton(reader);
        });
    }
}
