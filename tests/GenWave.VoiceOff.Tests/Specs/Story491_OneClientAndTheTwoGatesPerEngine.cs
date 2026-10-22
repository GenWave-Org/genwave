// STORY-491 — One client, and the two gates per engine (gh-#614 · SPEC F214.1, F214.5, F214.6, F214.16 · PLAN T608, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureOneClientAndTheTwoGatesPerEngine
{
    private const string Line = "hello there";

    private sealed record Rendered(SpeechResult Result, string? RequestBody);

    private static async Task<Rendered> RenderAsync(TimeSpan delay = default)
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: delay);
        using HttpClient http = SpeechClient.CreateHttpClient();
        SpeechResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10))
            .RenderAsync(Line, "v1", 1.0, CancellationToken.None);
        return new Rendered(result, stub.Requests.SpeechBody);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the profile shape loads
    public sealed class ScenarioTheProfileShapeLoads
    {
        private readonly EngineProfileSet set = EngineProfileSet.Load(RealEngines.Directory);

        [Fact]
        public void KokoroIsTheSingleBaseline() => Assert.Equal("kokoro", set.Baseline.Slug);
    }

    // AC2 — the client speaks the contract
    public sealed class ScenarioTheClientSpeaksTheContract : IAsyncLifetime
    {
        private Rendered rendered = new(new SpeechResult(default, 0, null), null);

        public async Task InitializeAsync() => rendered = await RenderAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRequestBodyIsInputVoiceSpeedResponseFormatWav()
        {
            using JsonDocument body = JsonDocument.Parse(rendered.RequestBody ?? "");
            Assert.Equal(
                """{"input":"hello there","voice":"v1","speed":1,"response_format":"wav"}""",
                JsonSerializer.Serialize(body.RootElement));
        }

        [Fact]
        public void TheBytesReturnedAreTheStubS() => Assert.Equal(EngineStubServer.Audio, rendered.Result.Audio.ToArray());
    }

    // AC3 — the client times the render
    public sealed class ScenarioTheClientTimesTheRender : IAsyncLifetime
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(150);
        private Rendered rendered = new(new SpeechResult(default, 0, null), null);

        public async Task InitializeAsync() => rendered = await RenderAsync(Delay);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheResultCarriesWallClockMillisecondsAtLeastThatDelay()
        {
            SpeechResult result = rendered.Result;
            Assert.True(result.WallMilliseconds >= Delay.TotalMilliseconds, $"wall was {result.WallMilliseconds} ms");
        }
    }

    // AC4 — voices are listed
    public sealed class ScenarioVoicesAreListed : IAsyncLifetime
    {
        private VoiceListResult listed = new([], null);

        public async Task InitializeAsync() => listed = await ListAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        private static async Task<VoiceListResult> ListAsync()
        {
            await using EngineStubServer stub = await EngineStubServer.StartAsync();
            using HttpClient http = SpeechClient.CreateHttpClient();
            return await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10)).ListVoicesAsync(CancellationToken.None);
        }

        [Fact]
        public void ItReturnsAAndB() => Assert.Equal(["a", "b"], listed.VoiceIds);
    }

    // AC5 — distinct voices pass voiceSelect
    public sealed class ScenarioDistinctVoicesPassVoiceSelect
    {
        // Same-voice distance 0.1, cross-voice distance 1.0: 10x, over the 3x margin.
        private readonly GateResult gate = VoiceSelectGate.Evaluate(
            new AudioFingerprint([0.0, 0.0]), new AudioFingerprint([0.1, 0.0]), new AudioFingerprint([1.0, 0.0]));

        [Fact]
        public void ItPasses() => Assert.True(gate.Passed);
    }

    // AC6 — a re-pick replaces
    public sealed class ScenarioARePickReplaces
    {
        private readonly RenderLedger ledger = new();

        public ScenarioARePickReplaces()
        {
            EngineProfile first = EngineProfileReader.Parse(EngineProfileJson.Valid().ToJsonString(), "alpha.json");
            EngineProfile repicked = EngineProfileReader.Parse(
                EngineProfileJson.With(n => n["repick"] = JsonSerializer.SerializeToNode(
                    new { register = "bubbly", voice = "v9", reason = "v1 clipped on plosives" })), "alpha.json");
            ledger.Record(new RenderRecord(GauntletRegister.Bubbly, "line-1", RenderLane.Plain, first.VoiceFor(GauntletRegister.Bubbly), "a.wav"));
            ledger.Record(new RenderRecord(GauntletRegister.Bubbly, "line-2", RenderLane.Plain, first.VoiceFor(GauntletRegister.Bubbly), "b.wav"));
            ledger.Record(new RenderRecord(GauntletRegister.Bubbly, "line-1", RenderLane.Plain, repicked.VoiceFor(GauntletRegister.Bubbly), "c.wav"));
            ledger.Record(new RenderRecord(GauntletRegister.Bubbly, "line-1", RenderLane.Markup, repicked.VoiceFor(GauntletRegister.Bubbly), "d.wav"));
            ledger.Retain(repicked);
        }

        [Fact]
        public void OnlyTheRePickedVoiceSRendersAreKept() =>
            Assert.Equal([("v9", "c.wav")], ledger.Records.Where(r => r.Lane == RenderLane.Plain).Select(r => (r.Voice, r.AudioPath)));

        [Fact]
        public void TheMarkupLaneRenderOfTheSameLineIsKeptBesideThePlainOne() =>
            Assert.Equal(["c.wav", "d.wav"], ledger.Records.Select(r => r.AudioPath).Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — same audio fails voiceSelect
    public sealed class ScenarioSameAudioFailsVoiceSelect
    {
        private readonly GateResult gate = VoiceSelectGate.Evaluate(
            new AudioFingerprint([0.5, 0.5]), new AudioFingerprint([0.5, 0.5]), new AudioFingerprint([0.5, 0.5]));

        [Fact]
        public void VoiceSelectIs() => Assert.False(gate.Passed);
    }

    // AC8 — no container fails the gate
    public sealed class ScenarioNoContainerFailsTheGate : IDisposable
    {
        // Given a profile whose container is not running
        // When  the container gate is computed

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioNoContainerFailsTheGate()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            run = scenario.Run(
                new FakeDockerInspector(new ContainerState(true, false, "10.0.0.2")),
                scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public void ItIs()
        {
            JsonNode gates = JsonNode.Parse(File.ReadAllText(run.PathIn("gates.json"))) ?? throw new InvalidOperationException("gates.json is empty");
            Assert.Equal(false, gates["engines"]?[0]?["container"]?["passed"]?.GetValue<bool>());
        }

        [Fact]
        public void TheEngineGetsNoPairs() => Assert.False(Directory.Exists(run.PathIn(Path.Combine("wav", "alpha"))));

        public void Dispose() => scenario.Dispose();
    }

    // AC9 — an unreachable engine is recorded
    public sealed class ScenarioAnUnreachableEngineIsRecorded : IDisposable
    {
        // Given a profile whose endpoint refuses connections
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioAnUnreachableEngineIsRecorded()
        {
            scenario.AddEngine("alpha", new Uri($"http://127.0.0.1:{ClosedPort()}/"));
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public void TheRunFinishes() => Assert.Equal(0, run.Exit);

        [Fact]
        public void TheEngineRowShowsWithTheError() =>
            Assert.Contains(run.OutputLines, l => l.StartsWith("alpha: ❌ container: voices endpoint at http://127.0.0.1:", StringComparison.Ordinal) && l.Contains("failed:", StringComparison.Ordinal));

        // A port nothing listens on: bind a free one, then let it go.
        private static int ClosedPort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose() => scenario.Dispose();
    }

    // AC10 — two baselines are refused
    public sealed class ScenarioTwoBaselinesAreRefused : IDisposable
    {
        private readonly EngineDirectoryFixture directories = new();

        public void Dispose() => directories.Dispose();

        [Fact]
        public void LoadingFails()
        {
            string directory = directories.Create(
                ("alpha.json", EngineProfileJson.Valid("alpha").ToJsonString()),
                ("beta.json", EngineProfileJson.Valid("beta").ToJsonString()));

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => EngineProfileSet.Load(directory));
            Assert.True(
                ex.Message.Contains("alpha.json", StringComparison.Ordinal) && ex.Message.Contains("beta.json", StringComparison.Ordinal),
                $"The refusal must name both files: {ex.Message}");
        }
    }
}
