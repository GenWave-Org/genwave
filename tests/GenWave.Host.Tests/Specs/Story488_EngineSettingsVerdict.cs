// STORY-488 — Status says whether the engine runs the saved settings (gh-#879 · SPEC F213.4–F213.9 · PLAN T600–T602)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/status through WebApplicationFactory with a fake IEngineTuningReader and a
// FakeTimeProvider-driven probe; WARN/INFO read from a capturing logger provider. The gw_tuning command is
// proven against real Liquidsoap (engine image) — Integration.

using System.Net;
using System.Net.Sockets;
using System.Text;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSettingsVerdict
{
    const string PendingStatus = "pending: T602 — probe-cached verdict on /api/status + WARN/INFO (STORY-488)";

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

    public sealed class ScenarioStatusCarriesTheVerdict
    {
        // Given: WAF; fake reader reports GW_XFADE_MIN 2 against effective 3; one probe tick; GET /api/status

        /// <summary>AC5 — engine.settings is "restartNeeded"</summary>
        [Fact(Skip = PendingStatus)]
        public void Settings() => Assert.Fail(PendingStatus);

        /// <summary>AC5 — engine.differs is ["GW_XFADE_MIN"]</summary>
        [Fact(Skip = PendingStatus)]
        public void Differs() => Assert.Fail(PendingStatus);

        /// <summary>AC12 — the engine block holds only settings and differs</summary>
        [Fact(Skip = PendingStatus)]
        public void NoValuesLeak() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioStatusNeverOpensASocket
    {
        // Given: WAF; counting fake reader; one probe tick, counter reset; 5 × GET /api/status inside the interval

        /// <summary>AC6 — the reader count stays 0</summary>
        [Fact(Skip = PendingStatus)]
        public void ReaderNotCalled() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioEnteringRestartNeeded
    {
        // Given: WAF; fake reader scripted inSync → restartNeeded → restartNeeded; 3 probe ticks

        /// <summary>AC7 — exactly one WARN</summary>
        [Fact(Skip = PendingStatus)]
        public void OneWarn() => Assert.Fail(PendingStatus);

        /// <summary>AC7 — the WARN names GW_XFADE_MIN</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesTheKey() => Assert.Fail(PendingStatus);

        /// <summary>AC7 — the WARN names `docker compose restart engine`</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesTheFix() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioBackInSync
    {
        // Given: WAF; fake reader scripted restartNeeded → inSync; 2 probe ticks

        /// <summary>AC8 — exactly one INFO saying the engine settings are back in sync</summary>
        [Fact(Skip = PendingStatus)]
        public void OneInfo() => Assert.Fail(PendingStatus);
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
        [Fact(Skip = PendingStatus)]
        public void NoNewWarn() => Assert.Fail(PendingStatus);
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

    public sealed class ScenarioBeforeTheFirstProbe
    {
        // Given: WAF just booted; no probe tick; GET /api/status

        /// <summary>AC11 — engine.settings is "unknown"</summary>
        [Fact(Skip = PendingStatus)]
        public void Unknown() => Assert.Fail(PendingStatus);

        /// <summary>AC11 — status returns 200</summary>
        [Fact(Skip = PendingStatus)]
        public void StatusIs200() => Assert.Fail(PendingStatus);
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
