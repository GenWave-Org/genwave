using System.Text.Json.Nodes;
using GenWave.Loudness;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Render;

/// <summary>
/// The render verb end to end over stub engines, with the real loudness match. Each nested class is one run and its facts
/// (one assertion each); the run happens in the constructor.
/// </summary>
public static class RenderCommandTests
{
    private static IReadOnlyList<TimingRecord> RecordsOf(RenderRun run) =>
        new TimingLog(run.PathIn("timing.jsonl")).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static JsonNode ReadJson(RenderRun run, string file) =>
        JsonNode.Parse(File.ReadAllText(run.PathIn(file))) ?? throw new InvalidOperationException($"{file} is empty");

    private static string[] ThreeLines() => RenderScenario.ThreeLines();

    private static RenderRun StandardRun(RenderScenario scenario, EngineStubServer stub, string[]? lines = null, params string[] extra)
    {
        scenario.AddEngine("alpha", stub.BaseUri);
        return scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(lines ?? ThreeLines()), "cpu", extra);
    }

    public sealed class AMatchedRun : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly string[] wavs;
        private readonly double[] lufs;

        public AMatchedRun()
        {
            RenderRun run = StandardRun(scenario, scenario.StartStubAsync().GetAwaiter().GetResult());
            wavs = Directory.GetFiles(run.PathIn(Path.Combine("wav", "alpha")), "*.wav");
            lufs = wavs.Select(w => new FfmpegLoudnessAnalyzer().AnalyzeAsync(w, CancellationToken.None).GetAwaiter().GetResult().IntegratedLufs).ToArray();
        }

        [Fact]
        public void EveryLineHasAMatchedClip() => Assert.Equal(3, wavs.Length);

        [Fact]
        public void EveryMatchedClipMeasuresSixteenLufs() => Assert.All(lufs, l => Assert.InRange(l, -17, -15));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class TheManifest : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly JsonNode manifest;
        private readonly string gauntletHash;

        public TheManifest()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            string gauntlet = scenario.GauntletDirectory(ThreeLines());
            gauntletHash = GauntletSet.Load(gauntlet).ContentHash;
            manifest = ReadJson(scenario.Run(scenario.EnginesDirectory, gauntlet, "cpu"), "run.json");
        }

        [Fact]
        public void ItNamesTheRun() => Assert.Equal("20261022T193000Z-cpu", manifest["runId"]?.GetValue<string>());

        [Fact]
        public void ItNamesTheTier() => Assert.Equal("cpu", manifest["tier"]?.GetValue<string>());

        [Fact]
        public void ItNamesTheHost() => Assert.Equal("test-host", manifest["host"]?.GetValue<string>());

        [Fact]
        public void ItSaysWhenTheRunStarted() => Assert.Equal("2026-10-22T19:30:00+00:00", manifest["startedAt"]?.GetValue<string>());

        [Fact]
        public void ItCarriesTheGauntletHash() => Assert.Equal(gauntletHash, manifest["gauntletHash"]?.GetValue<string>());

        [Fact]
        public void ItSaysCorrectionsAreNotPending() => Assert.False(manifest["correctionsPending"]?.GetValue<bool>());

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ACrosstalkLine : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;
        private readonly EngineStubServer stub;
        private readonly TimingRecord record;

        public ACrosstalkLine()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            run = StandardRun(scenario, stub, [RenderScenario.CrosstalkLine("cross-1")]);
            record = Assert.Single(RecordsOf(run));
        }

        [Fact]
        public void ItsRecordIsOk() => Assert.True(record.Ok);

        [Fact]
        public void ItsRecordedAudioIsBothTurnsAndThePause() => Assert.InRange(record.AudioMs, 6500, 6700);

        [Fact]
        public void ItsMatchedClipIsBothTurnsAndThePause() =>
            Assert.InRange(WavInfo.Read(File.ReadAllBytes(run.PathIn(Path.Combine("wav", "alpha", "cross-1.wav"))), out _)?.DurationMs ?? 0, 6500, 6700);

        [Fact]
        public void EachTurnIsRenderedWithItsOwnRegistersVoice() => Assert.Equal(["v1", "v2"], stub.Requests.SpeechVoices);

        [Fact]
        public void NoRawTurnFileIsLeftBehind() =>
            Assert.Equal(["cross-1.wav"], Directory.GetFiles(run.PathIn(Path.Combine("raw", "alpha"))).Select(Path.GetFileName));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class EnginesRenderInSlugOrder : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public EnginesRenderInSlugOrder()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("zulu", stub.BaseUri);
            scenario.AddEngine("alpha-b", stub.BaseUri);
            scenario.AddEngine("alpha", stub.BaseUri);
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu", "--engine", "zulu", "--engine", "alpha", "--engine", "alpha-b");
        }

        [Fact]
        // The files sort alpha-b.json before alpha.json, so only the slug order puts alpha first.
        public void OneAfterTheOtherBySlug() => Assert.Equal(["alpha", "alpha-b", "zulu"], RecordsOf(run).Select(r => r.Engine).Distinct());

        public void Dispose() => scenario.Dispose();
    }

    public sealed class OnlyTheNamedEngine : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public OnlyTheNamedEngine()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddEngine("beta", stub.BaseUri);
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu", "--engine", "beta");
        }

        [Fact]
        public void IsRendered() => Assert.Equal(["beta"], RecordsOf(run).Select(r => r.Engine).Distinct());

        public void Dispose() => scenario.Dispose();
    }

    public sealed class AnUnknownEngine : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public AnUnknownEngine() => run = StandardRun(scenario, scenario.StartStubAsync().GetAwaiter().GetResult(), null, "--engine", "nope");

        [Fact]
        public void IsAUsageError() => Assert.Equal(2, run.Exit);

        [Fact]
        public void IsNamedInTheMessage() => Assert.Contains("'nope'", run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNothing() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ABrokenProfile : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineDirectoryFixture engines = new();
        private readonly RenderRun run;

        public ABrokenProfile()
        {
            scenario.StartStubAsync().GetAwaiter().GetResult();
            string dir = engines.Create(("alpha.json", EngineProfileJson.With(n => n.Remove("license"))));
            run = scenario.Run(dir, scenario.GauntletDirectory(ThreeLines()), "cpu");
        }

        [Fact]
        public void IsRefusedWithExit1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void IsRefusedNamingTheFile() => Assert.Contains("alpha.json", run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNothing() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose()
        {
            engines.Dispose();
            scenario.Dispose();
        }
    }

    public sealed class ABrokenGauntlet : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ABrokenGauntlet()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            string bad = scenario.GauntletDirectory([GauntletFixtureDirectory.Line("short-1", "LeadIn", "too short")]);
            run = scenario.Run(scenario.EnginesDirectory, bad, "cpu");
        }

        [Fact]
        public void IsRefusedWithExit1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void IsRefusedNamingTheLine() => Assert.Contains("short-1", run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNothing() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ARunFolderThatAlreadyExists : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer stub;
        private readonly RenderRun run;

        public ARunFolderThatAlreadyExists()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            Directory.CreateDirectory(Path.Combine(scenario.Root, "20261022T193000Z-cpu"));
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu");
        }

        [Fact]
        public void IsRefusedWithExit1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void ReachesNoEngine() => Assert.Equal(0, stub.Requests.TotalRequests);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ARootThatCannotBeCreated : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly string rootFile = Path.Combine(Path.GetTempPath(), "voiceoff-rootfile-" + Guid.NewGuid().ToString("N"));
        private readonly RenderRun run;

        public ARootThatCannotBeCreated()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            File.WriteAllText(rootFile, "not a folder");
            run = scenario.RunAt(
                rootFile, new FakeDockerInspector(new ContainerState(true, true, "10.0.0.2")), CancellationToken.None,
                scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void SaysSoInPlainWords() => Assert.StartsWith("Cannot create the run folder", run.Error, StringComparison.Ordinal);

        public void Dispose()
        {
            File.Delete(rootFile);
            scenario.Dispose();
        }
    }

    public sealed class AnEngineThatStopsAnswering : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer slow = EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        private readonly RenderRun run;
        private readonly IReadOnlyList<TimingRecord> records;

        public AnEngineThatStopsAnswering()
        {
            scenario.AddEngine("alpha", slow.BaseUri, timeoutSeconds: 1);
            string[] lines = [.. ThreeLines(), GauntletFixtureDirectory.Line("leadin-2", "LeadIn", RenderScenario.Eight), GauntletFixtureDirectory.Line("leadin-3", "LeadIn", RenderScenario.Eight)];
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(lines), "cpu");
            records = RecordsOf(run);
        }

        [Fact]
        public void TheRunStillExits0() => Assert.Equal(0, run.Exit);

        [Fact]
        public void EveryClipIsRecordedFailed() => Assert.Equal(5, records.Count(r => !r.Ok));

        [Fact]
        public void TheClipsAfterTheThirdStrikeAreMarkedStopped() => Assert.Equal(2, records.Count(r => r.Error == EngineRun.StoppedError));

        [Fact]
        public void TheEnginesRowSaysItStopped() =>
            Assert.Contains(run.OutputLines, l => l.StartsWith("alpha: ❌ engine stopped answering, ", StringComparison.Ordinal));

        [Fact]
        public void TheEnginesRowCountsTheFailedClips() =>
            Assert.Contains(run.OutputLines, l => l.StartsWith("alpha: ❌ ", StringComparison.Ordinal) && l.Contains(" 5 clips, 5 failed", StringComparison.Ordinal));

        public void Dispose()
        {
            slow.DisposeAsync().AsTask().GetAwaiter().GetResult();
            scenario.Dispose();
        }
    }

    public sealed class ACancelledRun : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer slow = EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        private readonly RenderRun run;

        public ACancelledRun()
        {
            scenario.AddEngine("alpha", slow.BaseUri);
            using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(1));
            run = scenario.Run(new FakeDockerInspector(new ContainerState(true, true, "10.0.0.2")), cancel.Token, scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu");
        }

        [Fact]
        public void Exits130() => Assert.Equal(130, run.Exit);

        [Fact]
        public void KeepsTheManifest() => Assert.True(File.Exists(run.PathIn("run.json")));

        public void Dispose()
        {
            slow.DisposeAsync().AsTask().GetAwaiter().GetResult();
            scenario.Dispose();
        }
    }

    public sealed class AProfileWithItsOwnSpeed : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer stub;

        public AProfileWithItsOwnSpeed()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri, speed: 1.25);
            scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(ThreeLines()), "cpu");
        }

        [Fact]
        public void SendsThatSpeedWithEveryRequest() => Assert.Equal([1.25], stub.Requests.SpeechSpeeds.Distinct());

        public void Dispose() => scenario.Dispose();
    }

    public sealed class TheBaselineAndAnotherEngineRenderingTheSameLine : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer baseline;
        private readonly EngineStubServer other;

        public TheBaselineAndAnotherEngineRenderingTheSameLine()
        {
            baseline = scenario.StartStubAsync().GetAwaiter().GetResult();
            other = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", baseline.BaseUri);
            scenario.AddEngine("beta", other.BaseUri);
            string line = GauntletFixtureDirectory.Line("leadin-1", "LeadIn", RenderScenario.TwoSentences);
            scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory([line]), "cpu");
        }

        [Fact]
        public void TheBaselineGetsTheStationsSpeechMarkup() => Assert.Contains("[pause:", baseline.Requests.SpeechInputs[^1], StringComparison.Ordinal);

        [Fact]
        public void TheOtherEngineGetsPlainText() => Assert.DoesNotContain("[pause:", other.Requests.SpeechInputs[^1], StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class TheLoudnessDecisionRecordedPerClip : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly IReadOnlyList<TimingRecord> records;

        public TheLoudnessDecisionRecordedPerClip() => records = RecordsOf(StandardRun(scenario, scenario.StartStubAsync().GetAwaiter().GetResult()));

        [Fact]
        public void EveryRecordCarriesTheLoudnessMatchesDecision() => Assert.Equal([false], records.Select(r => r.Linear).Distinct());

        public void Dispose() => scenario.Dispose();
    }
}
