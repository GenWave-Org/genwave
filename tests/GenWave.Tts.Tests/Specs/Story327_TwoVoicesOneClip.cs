// STORY-327 — Two voices, one clip (gh-#385 · SPEC F127.5/.6 · PLAN VQ-i, T284)
//
// BDD specification — xUnit, LIVE as of T284. The craft target (Dean's word): reaction lines and
// interruption timing — the walkie-talkie turn-taking tell is what assembly exists to kill. Each
// line renders through the ONE funnel with ITS speaker's TtsRenderContext (F97.6 carriage, per
// line); ffmpeg assembles ONE asset the playout pipeline treats like any segment — no engine
// change, no multi-source mixing at air time. F99 extends per line: both voices or nobody. One
// assertion per Fact where the fact names one thing; a couple of facts here assert a small,
// inseparable pair (e.g. "rules AND pace", "bounded AND actually jittered") because that pairing is
// the whole content of what the fact's own name promises. Happy first; sad segregated. The T288
// wire acceptance is a production check, not here.
//
// FakeCrosstalkVoiceSynthesizer (not the shared FakeTtsSynthesizer) writes REAL, non-zero-duration
// tone WAVs — CrosstalkAssembler's ffmpeg delay/mix step needs genuine audio to position and sum;
// a zero-sample WAV concatenates/mixes to nothing. These facts invoke real ffmpeg/ffprobe (the
// house norm for this class of spec — see GenWave.MediaLibrary.Tests' own analyzer specs,
// Story003_SharedLoudnessAnalyzer.cs / Story016_FfmpegCueAnalyzer.cs).

namespace GenWave.Tts.Tests.Specs;

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

public static class FeatureTwoVoicesOneClip
{
    // ── Shared fixtures ─────────────────────────────────────────────────────

    static readonly PersonaCard HostCard = MakeCard(
        "Neon Nightowl", "host_voice", pace: 0.9,
        pronunciations: [new ContextPronunciationRule("GenWave", "GenWave", "gɛnˈweɪv")]);

    static readonly PersonaCard NeighborCard = MakeCard(
        "Daybreak Dana", "neighbor_voice", pace: 1.4,
        pronunciations: [new ContextPronunciationRule("Dana", "Dana", "ˈdɑːnə")]);

    static PersonaCard MakeCard(string name, string voiceId, double pace, IReadOnlyList<ContextPronunciationRule> pronunciations) =>
        new(PersonaCard.CurrentSchemaVersion, name, Tagline: "", Soul: name, Quirks: [],
            new VoiceSpec("kokoro", voiceId, pace, "en"), EnergyDisposition: 0, Lore: [], Corrections: [],
            Pronunciations: pronunciations);

    static CrosstalkAiredScript ThreeLineScript() => new(
    [
        new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hey, welcome back to the show.", IsInterjection: false),
        new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "Great to drop in tonight.", IsInterjection: false),
        new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Always good to have you around.", IsInterjection: false),
    ]);

    /// <summary>SPEC F127.4's own upper bound (8 lines, 7 transitions) — enough transitions that
    /// "not all gaps are identical" is a meaningful, not-by-luck assertion.</summary>
    static CrosstalkAiredScript EightLineScript() => new(
    [
        .. Enumerable.Range(0, 8).Select(i => new CrosstalkAiredLine(
            i % 2 == 0 ? CrosstalkSpeaker.Host : CrosstalkSpeaker.Neighbor,
            $"Line number {i + 1}.",
            IsInterjection: false)),
    ]);

    /// <summary>
    /// The one constructor arg list in this file (mirrors Story326_BoothWritesForTwo's own
    /// BuildWriterWithRingAndLogger idiom) — every scenario below builds its own
    /// <see cref="CrosstalkAssembler"/> from this rather than a second copy of the wiring.
    /// <paramref name="durationTargetSeconds"/> is the only knob most scenarios need to vary.
    /// </summary>
    static (CrosstalkAssembler Assembler, FakeCrosstalkVoiceSynthesizer Synth, FakeLoudnessAnalyzer Loudness,
        FakeCueAnalyzer Cue, CapturingLogger<CrosstalkAssembler> Logger, string CacheRoot) BuildAssembler(
            int durationTargetSeconds = 25)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var synth = new FakeCrosstalkVoiceSynthesizer();
        var loudnessAnalyzer = new FakeLoudnessAnalyzer();
        var cueAnalyzer = new FakeCueAnalyzer();
        var pronunciations = new PronunciationRuleProvider(
            new TestOptionsMonitor<TtsPronunciationsOptions>(new TtsPronunciationsOptions()),
            NullLogger<PronunciationRuleProvider>.Instance);
        var logger = new CapturingLogger<CrosstalkAssembler>();
        var crosstalkMonitor = new TestOptionsMonitor<CrosstalkOptions>(
            new CrosstalkOptions { DurationTargetSeconds = durationTargetSeconds });
        var ttsMonitor = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot });
        // A FakeAudioMixer that is never called: the two-voice AssembleAsync path (this suite's own
        // subject) never touches IAudioMixer at all — only the widened cast path (Story391,
        // AssembleCastAsync) does. Present here purely to satisfy the constructor.
        var assembler = new CrosstalkAssembler(
            synth, pronunciations, loudnessAnalyzer, cueAnalyzer, new FakeAudioMixer(), ttsMonitor, crosstalkMonitor, logger);
        return (assembler, synth, loudnessAnalyzer, cueAnalyzer, logger, cacheRoot);
    }

    static void CleanUp(string cacheRoot, FakeCrosstalkVoiceSynthesizer synth)
    {
        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioEveryLineRidesItsOwnSpeakersContext : IAsyncLifetime
    {
        FakeCrosstalkVoiceSynthesizer synth = null!;
        string cacheRoot = "";

        public async Task InitializeAsync()
        {
            // Given a validated script...
            CrosstalkAssembler assembler;
            (assembler, synth, _, _, _, cacheRoot) = BuildAssembler();

            // When the lines render...
            await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            CleanUp(cacheRoot, synth);
            return Task.CompletedTask;
        }

        [Fact]
        public void A_hosts_line_renders_with_the_hosts_rules_and_pace()
        {
            // Then the host's render carries the HOST's resolved pronunciation rules and pace —
            // never the neighbor's.
            var hostContext = synth.Contexts[0];
            Assert.Equal(HostCard.Voice.VoiceId, hostContext.Voice);
            Assert.Equal(HostCard.Voice.Pace, hostContext.Pace);
            Assert.Contains(hostContext.Rules, rule => rule.Pattern == "GenWave");
            Assert.DoesNotContain(hostContext.Rules, rule => rule.Pattern == "Dana");
        }

        [Fact]
        public void A_neighbors_line_renders_with_the_neighbors_rules_and_pace()
        {
            var neighborContext = synth.Contexts[1];
            Assert.Equal(NeighborCard.Voice.VoiceId, neighborContext.Voice);
            Assert.Equal(NeighborCard.Voice.Pace, neighborContext.Pace);
            Assert.Contains(neighborContext.Rules, rule => rule.Pattern == "Dana");
            Assert.DoesNotContain(neighborContext.Rules, rule => rule.Pattern == "GenWave");
        }
    }

    public sealed class ScenarioAssemblyBreathes : IDisposable
    {
        readonly CrosstalkAssembler assembler;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly string cacheRoot;

        public ScenarioAssemblyBreathes() => (assembler, synth, _, _, _, cacheRoot) = BuildAssembler();

        public void Dispose() => CleanUp(cacheRoot, synth);

        [Fact]
        public async Task Assembly_produces_exactly_one_audio_asset()
        {
            // Given all lines rendered, when the exchange is assembled...
            var result = await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None);

            // Then one audio asset results — the only file under the crosstalk cache directory.
            Assert.IsType<CrosstalkAssemblyResult.Assembled>(result);
            Assert.Single(Directory.GetFiles(Path.Combine(cacheRoot, "crosstalk")));
        }

        [Fact]
        public void Inter_line_gaps_are_jittered_within_the_bounded_range()
        {
            // Given a validated script's own gap plan...
            var script = EightLineScript();
            var seed = CrosstalkTimeline.ComputeSeed(script);

            // When the exchange's inter-line gaps are computed...
            var gaps = CrosstalkTimeline.ComputeGapsSeconds(script.Lines.Count - 1, seed);

            // Then every gap lands in the ~0.2-0.8s bounded range, and they are not all identical —
            // uniform gaps are the second-biggest TTS-dialogue tell this jitter exists to kill.
            Assert.All(gaps, gap => Assert.InRange(gap, CrosstalkTimeline.MinGapSeconds, CrosstalkTimeline.MaxGapSeconds));
            Assert.True(gaps.Distinct().Count() > 1);
        }

        [Fact]
        public void An_interjection_overlaps_the_prior_lines_tail_by_a_bounded_offset()
        {
            // Given the previous line ends at 5.0s...
            const double previousLineEnd = 5.0;

            // When an interjection line's start is planned against it...
            var start = CrosstalkTimeline.ComputeLineStartSeconds(previousLineEnd, isInterjection: true, gapSeconds: 0.5);

            // Then it starts BEFORE that tail, by a bounded, LITERAL offset (SPEC F127.6's own
            // ~0.35s figure) — never the ordinary jittered gap (0.5s here) an alternating line would
            // use instead, and never merely "whatever InterjectionOverlapSeconds happens to hold"
            // (T284 review F1b: asserting against the constant under test proves nothing about it).
            Assert.InRange(previousLineEnd - start, 0.2, 0.5);
        }

        [Fact]
        public void Gap_planning_is_deterministic_per_script_content()
        {
            // Given two DISTINCT CrosstalkAiredScript instances built from IDENTICAL content, and a third,
            // genuinely different script (T284 review F2 — a killer proved unseeding
            // CrosstalkTimeline.ComputeGapsSeconds's Random survived undetected)...
            var scriptA = ThreeLineScript();
            var scriptB = ThreeLineScript();
            var differentScript = EightLineScript();

            // When each script's own gap sequence is planned from its own content-derived seed...
            var gapsA = CrosstalkTimeline.ComputeGapsSeconds(scriptA.Lines.Count - 1, CrosstalkTimeline.ComputeSeed(scriptA));
            var gapsB = CrosstalkTimeline.ComputeGapsSeconds(scriptB.Lines.Count - 1, CrosstalkTimeline.ComputeSeed(scriptB));
            var gapsDifferent = CrosstalkTimeline.ComputeGapsSeconds(
                scriptA.Lines.Count - 1, CrosstalkTimeline.ComputeSeed(differentScript));

            // Then IDENTICAL content always plans the IDENTICAL sequence — re-assembling the same
            // script (a retry, a re-run) reproduces byte-identical timing (SPEC F127.6) — and
            // DIFFERENT content plans a different one; an unseeded `new Random()` would fail the
            // first assertion (two fresh instances never draw the same sequence) while still passing
            // the second, which is why both live in the one fact.
            Assert.Equal(gapsA, gapsB);
            Assert.NotEqual(gapsA, gapsDifferent);
        }

        [Fact]
        public async Task An_interjection_shortens_the_assembled_clip_by_the_replaced_gap_plus_the_overlap()
        {
            // Given two two-line scripts, IDENTICAL except the second line's IsInterjection flag —
            // the one variable between them (T284 review F1a: no existing fact assembled a script
            // WITH an interjection at all, so a mutation ignoring IsInterjection entirely, or
            // zeroing InterjectionOverlapSeconds, ran the whole suite green). A 1s-per-line tone
            // (longer than InterjectionOverlapSeconds) keeps the interjection's start comfortably
            // off the zero floor.
            synth.LineDurationSeconds = 1.0;
            var withoutInterjection = new CrosstalkAiredScript(
            [
                new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hey, welcome back to the show.", IsInterjection: false),
                new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "Great to drop in tonight.", IsInterjection: false),
            ]);
            var withInterjection = withoutInterjection with
            {
                Lines = [withoutInterjection.Lines[0], withoutInterjection.Lines[1] with { IsInterjection = true }],
            };

            // When both are assembled by the SAME assembler (no shared state crosses calls)...
            var resultWithout = Assert.IsType<CrosstalkAssemblyResult.Assembled>(
                await assembler.AssembleAsync(new CrosstalkAssemblyRequest(withoutInterjection, HostCard, NeighborCard), CancellationToken.None));
            var durationWithoutSec = await ProbeDurationSecondsAsync(resultWithout.Path);

            var resultWith = Assert.IsType<CrosstalkAssemblyResult.Assembled>(
                await assembler.AssembleAsync(new CrosstalkAssemblyRequest(withInterjection, HostCard, NeighborCard), CancellationToken.None));
            var durationWithSec = await ProbeDurationSecondsAsync(resultWith.Path);

            // Then the interjecting clip is shorter than its non-interjecting twin, by the SAME
            // relationship this fact's own script exercises (a replaced ordinary gap + the fixed
            // interjection overlap, ~555ms for this transition) — asserted as a relationship against
            // the assembler's OWN planner math for this exact transition, never a magic number.
            Assert.True(durationWithSec < durationWithoutSec);

            var replacedGapSeconds = CrosstalkTimeline.ComputeGapsSeconds(
                transitionCount: 1, CrosstalkTimeline.ComputeSeed(withoutInterjection))[0];
            var expectedDeltaSeconds = replacedGapSeconds + CrosstalkTimeline.InterjectionOverlapSeconds;
            var actualDeltaSeconds = durationWithoutSec - durationWithSec;
            Assert.InRange(actualDeltaSeconds, expectedDeltaSeconds - 0.15, expectedDeltaSeconds + 0.15);
        }
    }

    public sealed class ScenarioTheClipIsAFirstClassSegment : IDisposable
    {
        readonly CrosstalkAssembler assembler;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly FakeLoudnessAnalyzer loudness;
        readonly string cacheRoot;

        public ScenarioTheClipIsAFirstClassSegment() => (assembler, synth, loudness, _, _, cacheRoot) = BuildAssembler();

        public void Dispose() => CleanUp(cacheRoot, synth);

        [Fact]
        public async Task The_assembled_clip_is_loudness_measured_like_any_segment()
        {
            // Given an assembled exchange, when it enters the cache...
            var result = await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None);

            // Then it is loudness-measured exactly like any single-voice segment — the SAME
            // analyzer TtsSegmentSource uses, run against the ASSEMBLED path, not a per-line file.
            var assembled = Assert.IsType<CrosstalkAssemblyResult.Assembled>(result);
            Assert.Equal(assembled.Path, loudness.LastPath);
            Assert.Equal(loudness.Loudness, assembled.Loudness);
        }
    }

    public sealed class ScenarioDurationMsIsCueDerived : IDisposable
    {
        readonly CrosstalkAssembler assembler;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly FakeCueAnalyzer cue;
        readonly string cacheRoot;

        public ScenarioDurationMsIsCueDerived() => (assembler, synth, _, cue, _, cacheRoot) = BuildAssembler();

        public void Dispose() => CleanUp(cacheRoot, synth);

        [Fact]
        public async Task DurationMs_is_derived_from_the_cue_analyzers_own_cue_out_when_cue_analysis_succeeds()
        {
            // Given cue analysis reports a cue-out at 7.25s (T284 review F4, ORCHESTRATOR RULING:
            // DurationMs mirrors the house shape TtsSegmentSource/SafeSegmentAuthor already use —
            // BuildInsert's own remarks — not the ffprobe container-duration read the ceiling check
            // takes internally)...
            cue.Returns(new CuePoints(0.0, 7.25));

            // When the exchange is assembled...
            var result = Assert.IsType<CrosstalkAssemblyResult.Assembled>(
                await assembler.AssembleAsync(new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None));

            // Then DurationMs is the cue-out's own value, in milliseconds.
            Assert.Equal(7250, result.DurationMs);
        }

        [Fact]
        public async Task DurationMs_is_null_when_cue_analysis_finds_no_cue()
        {
            // Given cue analysis finds no cue points at all (cue analysis never gates readiness)...
            cue.Returns(null);

            // When the exchange is assembled...
            var result = Assert.IsType<CrosstalkAssemblyResult.Assembled>(
                await assembler.AssembleAsync(new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None));

            // Then DurationMs is null too — there is no OTHER duration source this result carries.
            Assert.Null(result.DurationMs);
        }
    }

    public sealed class ScenarioTheMixNeverClips : IDisposable
    {
        readonly CrosstalkAssembler assembler;
        readonly FakeCrosstalkVoiceSynthesizer synth;
        readonly string cacheRoot;

        public ScenarioTheMixNeverClips() => (assembler, synth, _, _, _, cacheRoot) = BuildAssembler();

        public void Dispose() => CleanUp(cacheRoot, synth);

        [Fact]
        public async Task Two_full_scale_voices_overlapping_do_not_clip_into_flat_tops()
        {
            // Given two genuinely FULL-SCALE tones (T284 review F6) — the worst case an
            // interjection's own ~0.35s overlap can produce, two 0 dBFS signals genuinely summed...
            synth.Amplitude = 1.0;
            synth.LineDurationSeconds = 1.0;
            var script = new CrosstalkAiredScript(
            [
                new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hey, welcome back to the show.", IsInterjection: false),
                new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "Great to drop in tonight.", IsInterjection: true),
            ]);

            // When the exchange is assembled...
            var result = Assert.IsType<CrosstalkAssemblyResult.Assembled>(
                await assembler.AssembleAsync(new CrosstalkAssemblyRequest(script, HostCard, NeighborCard), CancellationToken.None));

            // Then the assembled mix does not clip into flat-tops: ffmpeg's own astats "Flat factor"
            // (average length, in samples, of runs of consecutive IDENTICAL samples at min/max level —
            // literal hard-clipping, not merely a loud peak) stays under 1.0 sample — a principled
            // bound, not a magic threshold. A post-quantization max_volume/peak-dBFS read is NOT this
            // check: pcm_s16le's own ceiling makes max_volume <= 0 dBFS true whether or not the signal
            // actually clipped (proven: swapping the alimiter stage for anull still reads <= 0 dBFS),
            // so it never distinguishes "the alimiter held" from "the alimiter never ran at all".
            var flatFactor = await ProbeFlatFactorAsync(result.Path);
            Assert.True(flatFactor < 1.0, $"Assembled mix's astats Flat factor was {flatFactor} — clipped into flat-tops.");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioBothVoicesOrNobody : IAsyncLifetime
    {
        FakeCrosstalkVoiceSynthesizer synth = null!;
        string cacheRoot = "";
        CrosstalkAssemblyResult result = null!;

        public async Task InitializeAsync()
        {
            // Given any line whose render fails F99's right-voice bar (the neighbor's line, here)...
            CrosstalkAssembler assembler;
            (assembler, synth, _, _, _, cacheRoot) = BuildAssembler();
            synth.ThrowOnCallNumber = 2;

            // When assembly is attempted...
            result = await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            CleanUp(cacheRoot, synth);
            return Task.CompletedTask;
        }

        [Fact]
        public void One_line_failing_the_right_voice_bar_discards_the_whole_exchange() =>
            // Then the whole exchange is discarded — no single-voice salvage, nothing airs.
            Assert.IsType<CrosstalkAssemblyResult.Discarded>(result);

        [Fact]
        public void A_discarded_exchange_leaves_no_asset_behind()
        {
            // Every per-line file this fake wrote before the failure is deleted, and no mixed
            // asset was ever written to the crosstalk cache directory (assembly never runs).
            Assert.All(synth.WrittenPaths, path => Assert.False(File.Exists(path)));
            var crosstalkDir = Path.Combine(cacheRoot, "crosstalk");
            Assert.False(Directory.Exists(crosstalkDir) && Directory.GetFiles(crosstalkDir).Length > 0);
        }
    }

    public sealed class ScenarioAGenericFailureLeavesNothingBehind : IAsyncLifetime
    {
        FakeCrosstalkVoiceSynthesizer synth = null!;
        string cacheRoot = "";
        string crosstalkDir = "";
        Exception? thrown;

        public async Task InitializeAsync()
        {
            // Given a loudness analyzer that throws AFTER the mix has already been written to disk
            // (T284 review F3: temp/partial files leaked on every non-cancellation exception; the
            // fix mirrors FfmpegAudioMixer.MixAsync's own idiom one project over)...
            CrosstalkAssembler assembler;
            FakeLoudnessAnalyzer loudness;
            (assembler, synth, loudness, _, _, cacheRoot) = BuildAssembler();
            loudness.ThrowOnNextCall = new InvalidOperationException("simulated loudness-analyzer failure");
            crosstalkDir = Path.Combine(cacheRoot, "crosstalk");

            // When assembly is attempted...
            thrown = await Record.ExceptionAsync(() =>
                assembler.AssembleAsync(new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None));
        }

        public Task DisposeAsync()
        {
            CleanUp(cacheRoot, synth);
            return Task.CompletedTask;
        }

        [Fact]
        public void The_failure_propagates_to_the_caller() => Assert.IsType<InvalidOperationException>(thrown);

        [Fact]
        public void No_line_file_or_partial_mix_is_left_behind()
        {
            // Every per-line render is deleted, and the mixed asset ffmpeg had already written is
            // deleted too — the crosstalk cache directory is left EMPTY, not merely absent.
            Assert.All(synth.WrittenPaths, path => Assert.False(File.Exists(path)));
            Assert.True(!Directory.Exists(crosstalkDir) || Directory.GetFiles(crosstalkDir).Length == 0);
        }
    }

    public sealed class ScenarioAtLeastTwoLinesAreRequired
    {
        [Fact]
        public async Task Fewer_than_two_lines_fails_fast()
        {
            // Given a script with only one line — CrosstalkAssemblyRequest is a public, unvalidated
            // record, so nothing upstream of AssembleAsync itself guarantees CrosstalkScriptWriter's
            // own 3-8 line invariant reached this call (T284 review F7)...
            var (assembler, synth, _, _, _, cacheRoot) = BuildAssembler();
            var oneLineScript = new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Solo.", IsInterjection: false)]);

            try
            {
                // When assembly is attempted, then it fails FAST, before any render or ffmpeg call.
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    assembler.AssembleAsync(new CrosstalkAssemblyRequest(oneLineScript, HostCard, NeighborCard), CancellationToken.None));
                Assert.Empty(synth.Contexts);
            }
            finally
            {
                CleanUp(cacheRoot, synth);
            }
        }
    }

    public sealed class ScenarioTheEstimateLied : IAsyncLifetime
    {
        FakeCrosstalkVoiceSynthesizer synth = null!;
        CapturingLogger<CrosstalkAssembler> logger = null!;
        string cacheRoot = "";
        CrosstalkAssemblyResult result = null!;

        public async Task InitializeAsync()
        {
            // Given a tiny 1s duration target and lines that render far past what the chars-only
            // estimate would predict — the unmodelled inter-line gaps SPEC F127.6's ceiling exists
            // to catch...
            CrosstalkAssembler assembler;
            (assembler, synth, _, _, logger, cacheRoot) = BuildAssembler(durationTargetSeconds: 1);
            synth.LineDurationSeconds = 1.0;

            // When the assembled clip's real duration is measured...
            result = await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(ThreeLineScript(), HostCard, NeighborCard), CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            CleanUp(cacheRoot, synth);
            return Task.CompletedTask;
        }

        [Fact]
        public void A_clip_past_one_point_five_times_the_target_is_discarded()
        {
            var discarded = Assert.IsType<CrosstalkAssemblyResult.Discarded>(result);
            Assert.Contains("exceeds", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void The_discard_logs_both_the_estimated_and_actual_durations()
        {
            var infoEntry = Assert.Single(logger.Entries, candidate => candidate.Level == LogLevel.Information);
            Assert.Contains("assembled duration", infoEntry.Message, StringComparison.Ordinal);
            Assert.Contains("estimated", infoEntry.Message, StringComparison.Ordinal);
        }
    }

    // ── ffprobe/ffmpeg verification helpers — black-box: probe the rendered artifact, never the
    // assembler's internals (mirrors GenWave.MediaLibrary.Tests' own Story075_FfmpegAudioMixer idiom).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>ffmpeg's own <c>astats</c> filter's reported "Flat factor" — the average length, in
    /// samples, of runs of consecutive IDENTICAL samples sitting at the signal's min/max level (i.e.
    /// literal flat-tops, the hard-clipping signature) — for <paramref name="path"/>'s "Overall"
    /// statistics (astats logs one "Flat factor" line per channel, then a final one for "Overall";
    /// the last match in the output is always that Overall line).</summary>
    static async Task<double> ProbeFlatFactorAsync(string path)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-nostats");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-af");
        psi.ArgumentList.Add("astats=metadata=0");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        var matches = Regex.Matches(stderr, @"Flat factor:\s*(-?[\d.]+)");
        Assert.True(matches.Count > 0, $"No Flat factor reading in ffmpeg output for '{path}'.");
        return double.Parse(matches[^1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
