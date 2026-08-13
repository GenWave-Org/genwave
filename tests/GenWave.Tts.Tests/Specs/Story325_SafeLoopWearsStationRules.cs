// STORY-325 — The safe loop wears station rules (SPEC F126.3 · PLAN VQ-h, T276)
//
// BDD specification — xUnit. The seam audit's second bypass: SafeSegmentAuthor (the POST
// /api/safe-segments endpoint AND the boot seed — one code path) called the context-less overload,
// so safe clips rendered with empty rules forever. The fix authors through the context overload with
// the STATION's resolved rules — the safe loop is the station's voice; persona rules never apply
// (SafeSegmentAuthor has no collaborator capable of resolving one, unlike TtsSegmentSource/
// TtsPreviewController), pace stays 1.0 (no VoiceSpec at this layer to draw a rate from), and every
// authored render carries IsAudition = true (the T274 sibling ruling — authoring is not airing).
// One assertion per Fact; happy first; sad segregated. Fakes at every seam, mirroring
// Story078_SafeSegmentAuthor's own shape.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureSafeLoopWearsStationRules
{
    // ------------------------------------------------------------------
    // Shared fixture helpers — mirrors Story078_SafeSegmentAuthor.BuildAuthor, widened to accept a
    // caller-configured PronunciationRuleProvider so each fact can seed its own station rule set.
    // ------------------------------------------------------------------

    static SafeSegmentAuthor BuildAuthor(
        FakeTtsSynthesizer synth,
        PronunciationRuleProvider pronunciations,
        FakeAudioMixer mixer,
        FakeLoudnessAnalyzer loudness,
        FakeCueAnalyzer cue,
        FakeEnergyAnalyzer energy,
        FakeAuthoredCatalogWriter writer)
    {
        var opts = Options.Create(new TtsOptions { Format = "wav" });
        return new SafeSegmentAuthor(
            synth, pronunciations, mixer, loudness, cue, energy, writer, opts, NullLogger<SafeSegmentAuthor>.Instance);
    }

    static PronunciationRuleProvider StationRules(string? pronunciationsJson = null) =>
        new(
            new ChangeableOptionsMonitor<TtsPronunciationsOptions>(
                new TtsPronunciationsOptions { Pronunciations = pronunciationsJson }),
            NullLogger<PronunciationRuleProvider>.Instance);

    static SafeSegmentRequest Request(
        string authoredRoot,
        string text = "Please stand by.",
        long libraryId = 1,
        string stationName = "GenWave",
        string defaultVoice = "af_heart",
        string? title = null) =>
        new(text, libraryId, stationName, defaultVoice, authoredRoot,
            BedDuckDb: -12.0, BedPadSeconds: 1.5, Title: title);

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAuthoringResolvesStationRules : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task The_render_goes_through_the_context_overload_with_station_rules()
        {
            // Given saved station pronunciation rules
            var pronunciations = StationRules(
                """[{"pattern":"MacLeod","word":"MacLeod","ipa":"macleodIpa"}]""");
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);

            // When a safe segment is authored
            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            // Then the fake engine's captured context carries the station's resolved set — the
            // context overload, not the context-less bypass (which would leave LastContext null).
            Assert.NotNull(synth.LastContext);
            Assert.Contains(
                synth.LastContext!.Rules, r => r is { Pattern: "MacLeod", Ipa: "macleodIpa" });
        }

        [Fact]
        public async Task The_boot_seed_takes_the_same_path()
        {
            // Given the exact request shape SafeLoopSeeder.RenderSeedSegmentAsync builds (no bed,
            // no kind, its own SeedTitle) — AuthorAsync is the ONE code path both triggers call, so
            // there is no separate "seed" branch that could skip rule resolution.
            var pronunciations = StationRules(
                """[{"pattern":"MacLeod","word":"MacLeod","ipa":"macleodIpa"}]""");
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(
                Request(authoredRoot, title: "Please Stand By (Station Default)"), CancellationToken.None);

            // Then the seed's own clip carries the rules too.
            Assert.Contains(
                synth.LastContext!.Rules, r => r is { Pattern: "MacLeod", Ipa: "macleodIpa" });
        }

        [Fact]
        public async Task Pace_stays_the_default()
        {
            // The station has no VoiceSpec.Pace — 1.0 by construction (TtsRenderContext's own
            // default, left unset by SafeSegmentAuthor rather than resolved from anywhere).
            var pronunciations = StationRules();
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.Equal(1.0, synth.LastContext!.Pace);
        }

        [Fact]
        public async Task Authored_renders_are_marked_as_auditions()
        {
            // T274's sibling ruling on TtsRenderContext.IsAudition: authoring is not airing — an
            // authored clip's rule matches must never count toward PronunciationRuleHitReporter's
            // on-air observability, the SAME flag TtsPreviewController sets for the identical reason.
            var pronunciations = StationRules();
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.True(synth.LastContext!.IsAudition);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioPersonaRulesDoNotApply : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task Only_station_rules_are_resolved_for_a_safe_authoring()
        {
            // Given persona-scoped rules exist is, at this layer, an architectural non-event:
            // SafeSegmentAuthor holds no ActivePersonaPronunciationRulesCache (or any other
            // persona-reading collaborator) to draw a card rule from at all — unlike
            // TtsSegmentSource/TtsPreviewController, which both accept one. This fact pins that the
            // resolved set is EXACTLY the station's own declared rules, with nothing else folded in.
            var pronunciations = StationRules(
                """[{"pattern":"MacLeod","word":"MacLeod","ipa":"stationIpa"}]""");
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            // Then none of them reach the captured context beyond the station's own set — the safe
            // loop is the station's voice.
            var rule = Assert.Single(synth.LastContext!.Rules);
            Assert.Equal(new PronunciationRule("MacLeod", "MacLeod", "stationIpa"), rule);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioALaterRuleChangeDoesNotRewriteHistory : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();
        readonly ChangeableOptionsMonitor<TtsPronunciationsOptions> pronunciationsMonitor =
            new(new TtsPronunciationsOptions());

        /// <summary>
        /// Authors a first clip under the (empty) rule set in effect at the time, then saves a
        /// station rule afterward (the live <c>PUT /api/settings</c> reload path
        /// <see cref="PronunciationRuleProvider"/> subscribes to). Returns the first clip's own
        /// artifact path/bytes so each fact below can assert on it AFTER a second render actually
        /// runs — a bare byte-comparison with nothing executing in between would stay green under a
        /// mutation that broke re-authoring entirely (round-1 review finding).
        /// </summary>
        async Task<(string FirstArtifactPath, byte[] FirstBytes)> AuthorFirstClipThenSaveANewRuleAsync(
            SafeSegmentAuthor author)
        {
            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);
            var firstArtifactPath = mixer.LastRequest!.OutputPath;
            var firstBytes = await File.ReadAllBytesAsync(firstArtifactPath);

            pronunciationsMonitor.Change(new TtsPronunciationsOptions
            {
                Pronunciations = """[{"pattern":"MacLeod","word":"MacLeod","ipa":"newIpa"}]""",
            });

            return (firstArtifactPath, firstBytes);
        }

        [Fact]
        public async Task A_second_authoring_after_the_save_carries_the_new_rule()
        {
            // The fresh-per-call read pin: PronunciationRuleProvider.Current must be read live INSIDE
            // AuthorAsync, not captured once at construction — this is also the re-author fix itself
            // (SafeSegmentsController's own stated posture): author again, and the new rule applies.
            var pronunciations = new PronunciationRuleProvider(
                pronunciationsMonitor, NullLogger<PronunciationRuleProvider>.Instance);
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);
            await AuthorFirstClipThenSaveANewRuleAsync(author);

            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.Contains(synth.LastContext!.Rules, r => r is { Pattern: "MacLeod", Ipa: "newIpa" });
        }

        [Fact]
        public async Task The_first_artifacts_bytes_are_unchanged_by_the_later_save()
        {
            var pronunciations = new PronunciationRuleProvider(
                pronunciationsMonitor, NullLogger<PronunciationRuleProvider>.Instance);
            var author = BuildAuthor(synth, pronunciations, mixer, loudness, cue, energy, writer);
            var (firstArtifactPath, firstBytes) = await AuthorFirstClipThenSaveANewRuleAsync(author);

            // A second clip is authored under the new rule (the fresh-read half pinned by the
            // sibling fact above) — the already-persisted FIRST clip's own file must be left exactly
            // as it was: nothing re-renders it in place.
            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(firstArtifactPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
