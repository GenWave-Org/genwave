// STORY-391 — Spots render into the authored library (assembly half: AC1/AC2/AC3/AC5 · F161.2/.3 · pending T401)
// The worker half (AC4/AC6) lives in GenWave.Ads.Tests/Specs/Story391_AdSpotWorker.cs.
//
// Real ffmpeg throughout (STORY-391's own acceptance line), the Story327 harness precedent:
// FakeCrosstalkVoiceSynthesizer writes real, non-zero-duration tone WAVs so CrosstalkAssembler's
// adelay/amix/ffprobe steps have genuine audio to work with; loudness/cue MEASUREMENT stays faked
// (that machinery is Loudness's own project's concern, not this widened seam's). The bed-duck pass
// runs through the REAL GenWave.Loudness.FfmpegAudioMixer — AssembleCastAsync always calls IAudioMixer
// (even with no bed, to embed tags), so a fake that writes non-audio placeholder bytes would make the
// very next ffprobe read fail; only the genuine mixer produces a file ffprobe can actually read.

namespace GenWave.Tts.Tests.Specs;

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.Tts.Tests.Fakes;

public static class FeatureAdRenderAssembly
{
    // ── Shared fixtures/helpers ─────────────────────────────────────────────────────────────────

    const string StationName = "GWAV Test Station";
    const string SpotTitle = "Big Sale Spot";
    static readonly VoiceSpec AnnouncerVoice = new("kokoro", "announcer_voice", 1.0, "en");
    static readonly VoiceSpec Voice1 = new("kokoro", "voice1_voice", 1.2, "en");

    static IReadOnlyList<CastLine> OneLineScript() => [new CastLine("ANNOUNCER", "Everything must go.")];

    static IReadOnlyList<CastLine> TwoLineScript() =>
    [
        new CastLine("ANNOUNCER", "Come on down to the big sale."),
        new CastLine("VOICE1", "Prices you won't believe."),
    ];

    static IReadOnlyList<CastMember> OneVoiceCast() => [new CastMember("ANNOUNCER", AnnouncerVoice)];

    static IReadOnlyList<CastMember> TwoVoiceCast() =>
        [new CastMember("ANNOUNCER", AnnouncerVoice), new CastMember("VOICE1", Voice1)];

    /// <summary>Mirrors Story327's own <c>BuildAssembler</c> — every scenario below builds its own
    /// <see cref="CrosstalkAssembler"/> from this rather than a second copy of the wiring. The mixer
    /// is always the REAL <see cref="FfmpegAudioMixer"/> (see the file-level remarks above).</summary>
    static (CrosstalkAssembler Assembler, FakeCrosstalkVoiceSynthesizer Synth, FakeLoudnessAnalyzer Loudness,
        FakeCueAnalyzer Cue, string OutputDirectory) BuildAssembler()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var synth = new FakeCrosstalkVoiceSynthesizer();
        var loudnessAnalyzer = new FakeLoudnessAnalyzer();
        var cueAnalyzer = new FakeCueAnalyzer();
        var pronunciations = NoCorrections.PronunciationProvider();
        var ttsMonitor = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = outputDirectory, Format = "wav" });
        var crosstalkMonitor = new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions());
        var assembler = new CrosstalkAssembler(
            synth, pronunciations, loudnessAnalyzer, cueAnalyzer, new FfmpegAudioMixer(),
            ttsMonitor, crosstalkMonitor, NullLogger<CrosstalkAssembler>.Instance);
        return (assembler, synth, loudnessAnalyzer, cueAnalyzer, outputDirectory);
    }

    static (CastSegmentAuthor Author, FakeCrosstalkVoiceSynthesizer Synth, FakeAuthoredCatalogWriter Writer, string OutputDirectory) BuildAuthor()
    {
        var (assembler, synth, _, _, outputDirectory) = BuildAssembler();
        var writer = new FakeAuthoredCatalogWriter();
        var author = new CastSegmentAuthor(assembler, writer, NullLogger<CastSegmentAuthor>.Instance);
        return (author, synth, writer, outputDirectory);
    }

    static CastAssemblyRequest Request(
        IReadOnlyList<CastLine> lines, IReadOnlyList<CastMember> cast, string outputDirectory,
        double ceilingSeconds = 30.0, BedSpec? bed = null, double bedDuckDb = -12.0, double bedPadSeconds = 0.0) =>
        new(lines, cast, ceilingSeconds, new AudioTags(StationName, SpotTitle), outputDirectory, bed, bedDuckDb, bedPadSeconds);

    static AuthoredMediaInsert BuildInsert(CrosstalkAssemblyResult.Assembled assembled) =>
        new(
            Path: assembled.Path,
            Format: "wav",
            LibraryId: 7,
            SizeBytes: new FileInfo(assembled.Path).Length,
            Mtime: new FileInfo(assembled.Path).LastWriteTimeUtc,
            Tags: new AudioTags(StationName, SpotTitle),
            Loudness: assembled.Loudness,
            Cue: assembled.Cue,
            Energy: null,
            DurationMs: assembled.DurationMs,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Kind: ImagingKind.Ad);

    static void CleanUp(string outputDirectory, FakeCrosstalkVoiceSynthesizer synth)
    {
        if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCastIsVoicesNotPersonas : IDisposable
    {
        readonly CrosstalkAssembler assembler;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly string outputDirectory;

        public ScenarioTheCastIsVoicesNotPersonas() => (assembler, synth, _, _, outputDirectory) = BuildAssembler();

        public void Dispose() => CleanUp(outputDirectory, synth);

        [Fact]
        public async Task EachLineRendersWithItsOwnVoiceSpec()
        {
            // Given a two-VoiceSpec cast, no persona cards at all...
            await assembler.AssembleCastAsync(Request(TwoLineScript(), TwoVoiceCast(), outputDirectory), CancellationToken.None);

            // Then each per-line synth call carries THAT cast member's own VoiceId/Pace (F161.2).
            Assert.Equal(AnnouncerVoice.VoiceId, synth.Contexts[0].Voice);
            Assert.Equal(AnnouncerVoice.Pace, synth.Contexts[0].Pace);
            Assert.Equal(Voice1.VoiceId, synth.Contexts[1].Voice);
            Assert.Equal(Voice1.Pace, synth.Contexts[1].Pace);
        }

        [Fact]
        public async Task EveryLinePassesTheNormalizationChokepoint()
        {
            // Given the REAL NormalizingTtsSynthesizer (F68's one hand-off) decorating the fake, with
            // one operator correction configured...
            var corrections = new SpeechCorrectionProvider(
                new TestOptionsMonitor<TtsCorrectionsOptions>(
                    new TtsCorrectionsOptions { Corrections = """[{"from":"sale","to":"SALE EVENT"}]""" }),
                NullLogger<SpeechCorrectionProvider>.Instance);
            var innerSynth = new FakeCrosstalkVoiceSynthesizer();
            var normalizing = new NormalizingTtsSynthesizer(
                innerSynth, corrections, NoCorrections.PersonaCache(), new CorrectionsFiredStats(),
                NullLogger<NormalizingTtsSynthesizer>.Instance);
            var pronunciations = NoCorrections.PronunciationProvider();
            var normalizingAssembler = new CrosstalkAssembler(
                normalizing, pronunciations, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FfmpegAudioMixer(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = outputDirectory, Format = "wav" }),
                new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()), NullLogger<CrosstalkAssembler>.Instance);

            // When one line whose text names the corrected word renders...
            var oneLine = new[] { new CastLine("ANNOUNCER", "Come to the sale.") };
            await normalizingAssembler.AssembleCastAsync(Request(oneLine, OneVoiceCast(), outputDirectory), CancellationToken.None);

            // Then the text that reached the INNER synthesizer is already corrected — proving the
            // render passed through the normalization chokepoint, not straight to the engine.
            Assert.Contains("sale event", innerSynth.Contexts[0].Text, StringComparison.OrdinalIgnoreCase);
            CleanUp(outputDirectory, innerSynth);
        }

        [Fact]
        public async Task ASingleVoiceSpotAssembles()
        {
            // Given a 1-line, 1-voice announcer-only spot (crosstalk's own >=2-line floor is
            // deliberately relaxed on the widened request)...
            var result = await assembler.AssembleCastAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory), CancellationToken.None);

            // Then it assembles — legal, not an ArgumentException, not a discard.
            Assert.IsType<CrosstalkAssemblyResult.Assembled>(result);
        }
    }

    public sealed class ScenarioTheAuthoredTailLandsIt : IDisposable
    {
        readonly CastSegmentAuthor author;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly FakeAuthoredCatalogWriter writer;
        readonly string outputDirectory;

        public ScenarioTheAuthoredTailLandsIt() => (author, synth, writer, outputDirectory) = BuildAuthor();

        public void Dispose() => CleanUp(outputDirectory, synth);

        [Fact]
        public async Task OneMeasuredMediaRowLandsInTheAdsLibraryAsAdKind()
        {
            // Given a successful render, when it is authored...
            var result = await author.AuthorAsync(
                Request(TwoLineScript(), TwoVoiceCast(), outputDirectory),
                BuildInsert,
                confirmAsync: (_, _) => Task.FromResult(true),
                CancellationToken.None);

            // Then one library.media row exists, kind='ad', title/artist as the spot's own, and the
            // ARTIST is genuinely embedded in the artifact's own file metadata (F161.3), not merely
            // carried on the insert record.
            Assert.True(result.Succeeded);
            Assert.Equal(ImagingKind.Ad, writer.LastInsert!.Kind);
            Assert.Equal(SpotTitle, writer.LastInsert.Tags.Title);
            Assert.Equal(StationName, writer.LastInsert.Tags.Artist);
            var (artist, title) = await ProbeTagsAsync(writer.LastInsert.Path);
            Assert.Equal(StationName, artist);
            Assert.Equal(SpotTitle, title);
        }

        [Fact]
        public async Task MediaIdAndReadyStampInOneTransaction()
        {
            // Given the as-built two-round-trip shape (SPEC F161.3's own rider: no cross-schema
            // transaction is possible across the db/22 role boundary)...
            var order = new List<string>();

            var result = await author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory),
                BuildInsert,
                confirmAsync: (mediaId, _) =>
                {
                    order.Add($"confirm:{mediaId}");
                    return Task.FromResult(true);
                },
                CancellationToken.None);

            // Then: the insert lands FIRST, ineligible; only once the caller's own confirmation
            // (standing in for IAdSpotStore.MarkReadyAsync) reports success does eligibility flip —
            // the SAME media id throughout, never a fresh insert per stage.
            Assert.True(result.Succeeded);
            var mediaId = result.MediaId;
            Assert.Equal([$"confirm:{mediaId}"], order);
            Assert.Equal(mediaId, writer.LastSetEligibleMediaId);
            Assert.True(writer.LastSetEligibleValue);
            Assert.Equal(1, writer.SetEligibleCalls);
        }

        [Fact]
        public async Task AnOptionalBedMixesDuckedUnderTheVoices()
        {
            // Given a bed file padded 1s on each side of the cast (the AudioMixRequest bed path,
            // the SafeSegmentAuthor precedent)...
            var bedSynth = new FakeCrosstalkVoiceSynthesizer { LineDurationSeconds = 5.0 };
            var bedPath = await bedSynth.SynthesizeAsync(new TtsRenderContext("bed", "bed", null), CancellationToken.None);
            var bed = new BedSpec(bedPath, CueInSec: null, CueOutSec: null);

            var withBed = await author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory, bed: bed, bedPadSeconds: 1.0),
                BuildInsert, (_, _) => Task.FromResult(true), CancellationToken.None);
            var withBedDuration = await ProbeDurationSecondsAsync(writer.LastInsert!.Path);

            var (authorNoBed, synthNoBed, writerNoBed, outputNoBed) = BuildAuthor();
            try
            {
                var withoutBed = await authorNoBed.AuthorAsync(
                    Request(OneLineScript(), OneVoiceCast(), outputNoBed),
                    BuildInsert, (_, _) => Task.FromResult(true), CancellationToken.None);
                var withoutBedDuration = await ProbeDurationSecondsAsync(writerNoBed.LastInsert!.Path);

                // Then the bed path genuinely engaged — the padded artifact runs measurably longer
                // than its bed-less twin, never silently skipped.
                Assert.True(withBed.Succeeded);
                Assert.True(withoutBed.Succeeded);
                Assert.True(withBedDuration > withoutBedDuration);
            }
            finally
            {
                CleanUp(outputNoBed, synthNoBed);
                CleanUp(bedSynth.OutputDirectory, bedSynth);
            }
        }

        [Fact]
        public async Task ADeclinedConfirmationLeavesTheMediaRowIneligible()
        {
            // ⚠️ THE MATERIAL carry-forward pin (T398 review): a caller whose own confirmation
            // declines — standing in for IAdSpotStore.MarkReadyAsync returning false, or a crash
            // between the insert and that confirmation — must never leave an AIRABLE orphan.
            var result = await author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory),
                BuildInsert,
                confirmAsync: (_, _) => Task.FromResult(false),
                CancellationToken.None);

            // Then: the row was inserted ineligible, the flip never ran, and the caller sees a typed
            // failure — never a silently-airable row.
            Assert.False(result.Succeeded);
            Assert.Equal(CastSegmentFailureReason.ConfirmationFailed, result.FailureReason);
            Assert.False(writer.LastInsert!.Eligible);
            Assert.Equal(0, writer.SetEligibleCalls);
        }
    }

    public sealed class ScenarioTheInsertTailIsAllOrNothing : IDisposable
    {
        readonly CastSegmentAuthor author;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly FakeAuthoredCatalogWriter writer;
        readonly string outputDirectory;

        public ScenarioTheInsertTailIsAllOrNothing() => (author, synth, writer, outputDirectory) = BuildAuthor();

        public void Dispose() => CleanUp(outputDirectory, synth);

        [Fact]
        public async Task ABuildInsertFailureLeavesNoOrphanArtifactAndInsertsNothing()
        {
            // T401 review F3a: buildInsert now lives INSIDE the same cleanup boundary as
            // InsertAuthoredAsync itself — a caller bug in buildInsert must not leak the final
            // artifact the way it did when the call sat outside the try.
            var result = await author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory),
                buildInsert: _ => throw new InvalidOperationException("simulated buildInsert bug"),
                confirmAsync: (_, _) => Task.FromResult(true),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(CastSegmentFailureReason.InsertFailed, result.FailureReason);
            Assert.Equal(0, writer.Calls);
            Assert.Empty(Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory) : []);
        }

        [Fact]
        public async Task ACancellationRacingTheInsertLeavesNoOrphanArtifact()
        {
            // T401 review F3b: a cancel firing exactly at the insert call (never observed during
            // assembly, so nothing has committed yet) must still delete the final artifact — the
            // SafeSegmentAuthor.cs cancellation-cleanup precedent applied one stage later here.
            using var cts = new CancellationTokenSource();
            writer.CancelOnNextInsert = cts;

            await Assert.ThrowsAsync<OperationCanceledException>(() => author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory),
                BuildInsert,
                confirmAsync: (_, _) => Task.FromResult(true),
                cts.Token));

            Assert.Equal(0, writer.Calls);
            Assert.Empty(Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory) : []);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — all-or-nothing
    // ---------------------------------------------------------------------

    public sealed class ScenarioOverCeilingDiscards : IDisposable
    {
        readonly CastSegmentAuthor author;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly FakeAuthoredCatalogWriter writer;
        readonly string outputDirectory;

        public ScenarioOverCeilingDiscards() => (author, synth, writer, outputDirectory) = BuildAuthor();

        public void Dispose() => CleanUp(outputDirectory, synth);

        [Fact]
        public async Task AnOverLongArtifactIsDeletedAndTheSpotFailed()
        {
            // Given a spot whose real rendered duration will run past a tiny per-request ceiling —
            // the ceiling is per-request; the global Crosstalk:DurationTargetSeconds knob (never set
            // here) is never consulted...
            synth.LineDurationSeconds = 2.0;

            // When the render is authored...
            var result = await author.AuthorAsync(
                Request(OneLineScript(), OneVoiceCast(), outputDirectory, ceilingSeconds: 0.5),
                BuildInsert, (_, _) => Task.FromResult(true), CancellationToken.None);

            // Then: discarded (never trimmed), the reason names the ceiling, and nothing was ever
            // inserted — the artifact never reached the authored tail at all.
            Assert.False(result.Succeeded);
            Assert.Equal(CastSegmentFailureReason.Discarded, result.FailureReason);
            Assert.Contains("ceiling", result.FailureDetail, StringComparison.Ordinal);
            Assert.Equal(0, writer.Calls);
            Assert.Empty(Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory) : []);
        }

        [Fact]
        public async Task BedPaddingAloneCanPushPastTheCeiling()
        {
            // Given a voice line comfortably UNDER a real ceiling on its own (T401 review F5: proves
            // the ceiling gate measures the FINAL, bed-included artifact's real duration — never the
            // pre-bed raw mix, which alone would pass this exact ceiling)...
            synth.LineDurationSeconds = 0.3;
            var bedSynth = new FakeCrosstalkVoiceSynthesizer { LineDurationSeconds = 10.0 };
            var bedPath = await bedSynth.SynthesizeAsync(new TtsRenderContext("bed", "bed", null), CancellationToken.None);
            var bed = new BedSpec(bedPath, CueInSec: null, CueOutSec: null);

            try
            {
                // When it is authored with generous bed padding (2s each side — SPEC F27.4's
                // BedPadSeconds pads BOTH lead-in and tail-out) against a ceiling only the padding,
                // never the 0.3s voice alone, can breach...
                var result = await author.AuthorAsync(
                    Request(OneLineScript(), OneVoiceCast(), outputDirectory, ceilingSeconds: 1.0, bed: bed, bedPadSeconds: 2.0),
                    BuildInsert, (_, _) => Task.FromResult(true), CancellationToken.None);

                // Then it discards — the ceiling ordering is on the final artifact, not the raw mix.
                Assert.False(result.Succeeded);
                Assert.Equal(CastSegmentFailureReason.Discarded, result.FailureReason);
                Assert.Contains("ceiling", result.FailureDetail, StringComparison.Ordinal);
            }
            finally
            {
                CleanUp(bedSynth.OutputDirectory, bedSynth);
            }
        }
    }

    public sealed class ScenarioMidPipelineFailureLeavesNothing : IAsyncLifetime
    {
        CastSegmentAuthor author = null!;
        FakeCrosstalkVoiceSynthesizer synth = null!;
        FakeAuthoredCatalogWriter writer = null!;
        FakeLoudnessAnalyzer loudness = null!;
        string outputDirectory = "";
        CastSegmentAuthorResult result = null!;

        public async Task InitializeAsync()
        {
            // Given a loudness analyzer that throws AFTER both mix passes have already written to
            // disk (T284 review F3's own precedent, one project over) — a MEASURE-stage failure,
            // exercising CastSegmentAuthor's outer catch (AssemblyFailed), distinct from the
            // ceiling's own business Discarded path above...
            (var assembler, synth, loudness, _, outputDirectory) = BuildAssembler();
            loudness.ThrowOnNextCall = new InvalidOperationException("simulated loudness-analyzer failure");
            writer = new FakeAuthoredCatalogWriter();
            author = new CastSegmentAuthor(assembler, writer, NullLogger<CastSegmentAuthor>.Instance);

            // When the render is authored...
            result = await author.AuthorAsync(
                Request(TwoLineScript(), TwoVoiceCast(), outputDirectory),
                BuildInsert, (_, _) => Task.FromResult(true), CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            CleanUp(outputDirectory, synth);
            return Task.CompletedTask;
        }

        [Fact]
        public void ASynthesisMixOrMeasureFailureLeavesNoOrphanFiles()
        {
            // Every per-line render is deleted, and neither the raw pre-bed mix nor the final,
            // tag-embedded artifact is left behind — the output directory ends up empty, and nothing
            // was ever inserted.
            Assert.All(synth.WrittenPaths, path => Assert.False(File.Exists(path)));
            Assert.Empty(Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory) : []);
            Assert.Equal(0, writer.Calls);
        }

        [Fact]
        public void TheSpotFailsWithATypedReason()
        {
            Assert.False(result.Succeeded);
            Assert.Equal(CastSegmentFailureReason.AssemblyFailed, result.FailureReason);
            Assert.Contains("loudness-analyzer failure", result.FailureDetail, StringComparison.Ordinal);
        }
    }

    // ── ffprobe verification helpers — black-box: probe the rendered artifact, never the
    // assembler's internals (mirrors Story327's/Story075's own idiom). ────────────────────────────

    static async Task<double> ProbeDurationSecondsAsync(string path)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=duration");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return double.Parse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static async Task<(string? Artist, string? Title)> ProbeTagsAsync(string path)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format_tags=artist,title");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("default=noprint_wrappers=1");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        var artistMatch = Regex.Match(stdout, @"TAG:artist=(.*)");
        var titleMatch = Regex.Match(stdout, @"TAG:title=(.*)");
        return (
            artistMatch.Success ? artistMatch.Groups[1].Value.Trim() : null,
            titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : null);
    }
}
