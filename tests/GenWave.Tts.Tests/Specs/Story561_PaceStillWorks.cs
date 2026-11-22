// STORY-561 — Pace still works (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Activated by T769. Needs ffmpeg + ffprobe on PATH (the PR tier has them).

namespace GenWave.Tts.Tests.Specs;

using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePaceStillWorks
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    const double ToneHz = 440.0;
    const double ToneSeconds = 2.0;

    // A real 2 s, 440 Hz mono wav from ffmpeg's own sine source: deterministic, and it has a measurable pitch.
    static async Task<byte[]> ToneAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), "gw-story561-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await FfmpegProcess.RunFfmpegAsync(
                ["-nostdin", "-y", "-f", "lavfi", "-i", $"sine=frequency={ToneHz}:duration={ToneSeconds}:sample_rate=24000", "-ac", "1", file],
                CancellationToken.None).WaitAsync(Limit);
            return await File.ReadAllBytesAsync(file).WaitAsync(Limit);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // Rising zero crossings per second of the first channel of a wav: the dominant frequency of a pure tone.
    // Assumes 16-bit signed PCM (2 bytes a sample), which is what ffmpeg writes for these wavs.
    static double DominantHz(string path)
    {
        var wav = File.ReadAllBytes(path);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(22));
        var rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24));
        var at = 12;
        while (System.Text.Encoding.ASCII.GetString(wav, at, 4) != "data")
            at += 8 + BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(at + 4));
        var frames = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(at + 4)) / (2 * channels);
        var crossings = 0;
        var previous = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(at + 8));
        for (var i = 1; i < frames; i++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(at + 8 + i * 2 * channels));
            if (previous < 0 && sample >= 0)
                crossings++;
            previous = sample;
        }

        return crossings / ((double)frames / rate);
    }

    static string NewCacheRoot() => Path.Combine(Path.GetTempPath(), "gw-story561-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    static VoiceEngine Engine(VoiceEngineProfile profile) => new(profile.Name, $"http://{profile.Name}:8000", profile, 30);

    static SpeechEngineClient ClientFor(
        byte[] audio, string cacheRoot, PaceStretch? stretch = null, EngineRenderActivity? activity = null, TimeProvider? time = null)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(audio) }));
        var options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
        return new SpeechEngineClient(
            new HttpClient(handler), options, activity ?? new EngineRenderActivity(), timeProvider: time, paceStretch: stretch);
    }

    static TtsRenderContext At(double pace) => new("Hello there.", "v", Kind: null) { Pace = pace };

    // Stands in for ffmpeg: records the call and, like ffmpeg, writes the output (a copy of the input).
    sealed class RecordingRunner(Func<CancellationToken, Task>? during = null)
    {
        public List<IReadOnlyList<string>> Runs { get; } = [];

        public async Task Run(IReadOnlyList<string> args, CancellationToken ct)
        {
            Runs.Add(args);
            if (during is not null)
                await during(ct);
            File.Copy(args[args.ToList().IndexOf("-i") + 1], args[^1], overwrite: true);
        }

        public PaceStretch Stretch(ILogger? logger = null) => new(logger ?? NullLogger.Instance, Run);
    }

    // AC1, AC2 — pace 1.1 on chatterbox (ffmpeg on PATH)
    public sealed class ScenarioStretched : IAsyncLifetime
    {
        readonly string cacheRoot = NewCacheRoot();
        string plain = "";
        string quick = "";

        public async Task InitializeAsync()
        {
            var client = ClientFor(await ToneAsync(), cacheRoot);
            var chatterbox = Engine(VoiceEngineProfiles.Chatterbox);
            plain = await client.RenderOnAsync(chatterbox, At(1.0), CancellationToken.None).WaitAsync(Limit);
            quick = await client.RenderOnAsync(chatterbox, At(1.1), CancellationToken.None).WaitAsync(Limit);
        }

        public Task DisposeAsync()
        {
            Cleanup(cacheRoot);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task TheLengthIsAboutOneOverPace()
        {
            var unstretched = await FfmpegProcess.ProbeDurationSecondsAsync(plain, CancellationToken.None).WaitAsync(Limit);
            var stretched = await FfmpegProcess.ProbeDurationSecondsAsync(quick, CancellationToken.None).WaitAsync(Limit);

            Assert.InRange(stretched / unstretched, 1 / 1.1 - 0.02, 1 / 1.1 + 0.02);
        }

        [Fact]
        public void ThePitchMatchesTheUnstretchedRender()
        {
            var unstretched = DominantHz(plain);

            Assert.InRange(DominantHz(quick), unstretched * 0.98, unstretched * 1.02);
        }

        [Fact]
        public async Task TheStretchRunsBeforeTheLoudnessMeasure()
        {
            var root = NewCacheRoot();
            try
            {
                var analyzer = new DurationAtMeasureAnalyzer();
                var accessor = new FakeActivePersonaAccessor
                {
                    Card = new PersonaCard(1, "P", "t", "s", [], new VoiceSpec("", "v", 1.1, "en"), 0, [], []),
                };
                var options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = root, Format = "wav" });
                var active = TestActiveEngine.Named("chatterbox");
                var synth = new ActiveEngineSynthesizer(active, ClientFor(await ToneAsync(), root));
                var source = new TtsSegmentSource(
                    new FakeSegmentCopyWriter("Coming up next."), synth, analyzer, new FakeCueAnalyzer(),
                    NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                    NoCorrections.PersonaPronunciationCache(),
                    new ActivePersonaPaceCache(accessor, TimeProvider.System, NullLogger<ActivePersonaPaceCache>.Instance),
                    options, NullLogger<TtsSegmentSource>.Instance, active);

                var item = await source.RenderAsync(
                    new SegmentRequest(SegmentKind.StationId, "v", "GenWave", null, DateTimeOffset.UtcNow, "test-station"),
                    CancellationToken.None).WaitAsync(Limit);

                Assert.NotNull(item);
                Assert.InRange(analyzer.SecondsWhenMeasured, ToneSeconds / 1.1 - 0.05, ToneSeconds / 1.1 + 0.05);
            }
            finally
            {
                Cleanup(root);
            }
        }

        sealed class DurationAtMeasureAnalyzer : ILoudnessAnalyzer
        {
            public double SecondsWhenMeasured { get; private set; }

            public async Task<Loudness> AnalyzeAsync(string path, CancellationToken ct)
            {
                SecondsWhenMeasured = await FfmpegProcess.ProbeDurationSecondsAsync(path, ct);
                return new Loudness(-16.0, -1.0, true);
            }
        }
    }

    // Sad path
    // AC3–AC5
    public sealed class ScenarioNoStretch
    {
        static async Task<List<IReadOnlyList<string>>> RunsFor(VoiceEngineProfile profile, double pace)
        {
            var root = NewCacheRoot();
            try
            {
                var runner = new RecordingRunner();
                var client = ClientFor([1, 2, 3, 4], root, runner.Stretch());
                await client.RenderOnAsync(Engine(profile), At(pace), CancellationToken.None).WaitAsync(Limit);
                return runner.Runs;
            }
            finally
            {
                Cleanup(root);
            }
        }

        [Fact]
        public async Task PaceOneSkipsTheStep() =>
            Assert.Empty(await RunsFor(VoiceEngineProfiles.Chatterbox, 1.0));

        [Fact]
        public async Task PaceTwoIsClampedTo125() =>
            Assert.Contains("atempo=1.25", Assert.Single(await RunsFor(VoiceEngineProfiles.Chatterbox, 2.0)));

        [Fact]
        public async Task PaceHalfIsClampedTo08() =>
            Assert.Contains("atempo=0.8", Assert.Single(await RunsFor(VoiceEngineProfiles.Chatterbox, 0.5)));

        [Fact]
        public async Task KokoroNeverStretches() =>
            Assert.Empty(await RunsFor(VoiceEngineProfiles.Kokoro, 1.1));
    }

    // Hazard: the stretch is a nicety and a local step, never a reason to lose the line or blame the engine
    public sealed class ScenarioTheStretchIsASideStep : IDisposable
    {
        static readonly byte[] Original = [9, 8, 7, 6];
        readonly string cacheRoot = NewCacheRoot();

        public void Dispose() => Cleanup(cacheRoot);

        static PaceStretch Broken(ILogger? logger = null) =>
            new(logger ?? NullLogger.Instance, (_, _) => throw new InvalidOperationException("ffmpeg exited with code 1"));

        [Fact]
        public async Task AFailedStretchKeepsTheUnstretchedBytes()
        {
            var client = ClientFor(Original, cacheRoot, Broken());

            var path = await client.RenderOnAsync(Engine(VoiceEngineProfiles.Chatterbox), At(1.1), CancellationToken.None).WaitAsync(Limit);

            Assert.Equal(Original, await File.ReadAllBytesAsync(path).WaitAsync(Limit));
        }

        [Fact]
        public async Task AFailedStretchLogsAWarning()
        {
            var logger = new CapturingLogger<PaceStretch>();
            var client = ClientFor(Original, cacheRoot, Broken(logger));

            await client.RenderOnAsync(Engine(VoiceEngineProfiles.Chatterbox), At(1.1), CancellationToken.None).WaitAsync(Limit);

            Assert.Single(logger.Warnings);
        }

        [Fact]
        public async Task ACancelMidStretchLeavesNoFileInTheRenderDirectory()
        {
            using var cts = new CancellationTokenSource();
            var started = new TaskCompletionSource();
            var runner = new RecordingRunner(async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            });
            var client = ClientFor(Original, cacheRoot, runner.Stretch());

            var render = client.RenderOnAsync(Engine(VoiceEngineProfiles.Chatterbox), At(1.1), cts.Token);
            await started.Task.WaitAsync(Limit);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render.WaitAsync(Limit));

            Assert.Empty(Directory.GetFiles(cacheRoot, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task ACancelMidStretchIsNoEngineFault()
        {
            using var cts = new CancellationTokenSource();
            var started = new TaskCompletionSource();
            var activity = new EngineRenderActivity();
            var runner = new RecordingRunner(async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            });
            var client = ClientFor(Original, cacheRoot, runner.Stretch(), activity);
            var chatterbox = Engine(VoiceEngineProfiles.Chatterbox);

            var render = client.RenderOnAsync(chatterbox, At(1.1), cts.Token);
            await started.Task.WaitAsync(Limit);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render.WaitAsync(Limit));

            Assert.Equal(0, activity.Read(chatterbox.Identity).FailuresInARow);
        }

        [Fact]
        public async Task ASlowStretchAfterAFastRenderIsNotLate()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.Zero));
            var activity = new EngineRenderActivity(clock);
            var runner = new RecordingRunner(_ =>
            {
                clock.Advance(TimeSpan.FromMinutes(5)); // far past the engine's 30 s limit
                return Task.CompletedTask;
            });
            var client = ClientFor(Original, cacheRoot, runner.Stretch(), activity, clock);
            var chatterbox = Engine(VoiceEngineProfiles.Chatterbox);

            await client.RenderOnAsync(chatterbox, At(1.1), CancellationToken.None).WaitAsync(Limit);

            Assert.Equal(0, activity.Read(chatterbox.Identity).FailuresInARow);
        }
    }

    // Hazard: clips cached before the stretch existed must not air unstretched forever
    public sealed class ScenarioTheCacheEpoch
    {
        [Fact]
        public void MovesTheEpoch() =>
            Assert.EndsWith("+f219.31", TtsSegmentSource.MergePolicyVersion, StringComparison.Ordinal);
    }
}
