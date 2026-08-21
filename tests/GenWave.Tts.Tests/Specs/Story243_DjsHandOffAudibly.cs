// STORY-243 — DJs hand off audibly (SPEC F92, PLAN T123/T124)
//
// BDD specification — xUnit. T123's half: the copywriter kinds are pure GenWave.Tts seams
// (prompt content, template fallback, cache routing) — proven directly here against
// LlmPromptBuilder/PatterTemplateRenderer/TtsSegmentSource rather than through a real
// Orchestrator (this project has no ProjectReference to GenWave.Orchestration, mirroring
// Story228_RequestShoutOut.cs's own split). The remaining STORY-243 facts (a real playout
// run across a seeded boundary, F74 queue wiring, supersede, the degrade ladder) depend on
// the T124 producer and live in
// GenWave.Orchestration.Tests/Specs/Story243_DjsHandOffAudibly.cs instead.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureDjsHandOffAudibly
{
    const string StationClockLine = "Current date/time (station-local): irrelevant";
    const string StationId = "test-station";

    // 2026-07-27, station-local (STORY-214's FixedLocalNow idiom, T123 review finding) — a fixed
    // clock rather than DateTimeOffset.UtcNow so the golden-string prompt assertion below
    // (ScenarioRightVoicesRightNames.SignOffPromptMatchesExpectedContentByteForByte) has a stable
    // "Local time" line to pin against.
    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    static SegmentRequest HandoffRequest(SegmentKind kind, string? counterpartName) =>
        new(kind, "af_heart", "GenWave", Track: null, FixedLocalNow, StationId,
            PersonaName: null, CounterpartName: counterpartName);

    // -----------------------------------------------------------------------
    // AC2 (F92.2) — each prompt receives the counterpart's display name
    // -----------------------------------------------------------------------

    public sealed class ScenarioRightVoicesRightNames
    {
        [Fact]
        public void SignOffPromptNamesTheCounterpartWhenPresent()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOff, "Nite Owl"), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("Nite Owl", content);
        }

        [Fact]
        public void SignOffPromptGuidesMusicOnlyPhrasingWhenCounterpartAbsent()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOff, counterpartName: null), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("music-only", content);
            Assert.Contains("never invent a name, show, or time", content);
        }

        [Fact]
        public void SignOnPromptNamesTheCounterpartWhenPresent()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOn, "Daybreak Dana"), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("Daybreak Dana", content);
        }

        [Fact]
        public void SignOnPromptGuidesNonstopMusicPhrasingWhenCounterpartAbsent()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOn, counterpartName: null), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("nonstop music", content);
            Assert.Contains("never invent a DJ, show, or time", content);
        }

        [Fact]
        public void SignOffPromptMatchesExpectedContentByteForByte()
        {
            // Golden-string pin (T123 review finding): the swap-the-name Replace() trick this
            // replaces only proved the name was the ONLY thing that varied between two renders — a
            // mutant that injected extra request.LocalNow-derived text into BOTH renders would have
            // survived it. Asserting the FULL BuildUserContent output against a fixed clock instead
            // means any added, removed, or reordered line fails this fact, not just a wrong name.
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOff, "Nite Owl"), StationClockLine, previouslyVoicedTasteNotes: []);

            const string Expected =
                "Station: GenWave\n" +
                "Local time: 2026-07-27 09:00\n" +
                "Current date/time (station-local): irrelevant\n" +
                "Segment: sign-off as you close out your shift on air.\n" +
                "Handoff note: Nite Owl is up next - you may name them as you sign off (e.g. " +
                "\"stick around, Nite Owl is coming up\"). Only use the name given here; never " +
                "invent a show name, time, or event for them.";

            Assert.Equal(Expected, content);
        }

        [Fact]
        public void SignOffPromptStatesItsOwnSegmentRole()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOff, "Nite Owl"), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("sign-off", content);
        }

        [Fact]
        public void SignOnPromptStatesItsOwnSegmentRole()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOn, "Daybreak Dana"), StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.Contains("sign-on", content);
        }
    }

    // -----------------------------------------------------------------------
    // Template fallback — deterministic rung for both kinds, both variants. Still needs to render
    // CORRECT text even though a handoff's own fallback result is later dropped before air (see
    // ScenarioNonLlmAuthoredCopyNeverAirs below): DegradationGatedCopyWriter's Hard/off-cadence
    // paths route straight to this renderer, bypassing LlmCopyWriter entirely.
    // -----------------------------------------------------------------------

    public sealed class ScenarioTemplateFallbacks
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void SignOffTemplateNamesTheCounterpartWhenPresent()
        {
            var text = renderer.Expand(HandoffRequest(SegmentKind.SignOff, "Nite Owl"));
            Assert.Contains("Nite Owl", text);
        }

        [Fact]
        public void SignOffTemplateStaysMusicOnlyWhenCounterpartAbsent()
        {
            var text = renderer.Expand(HandoffRequest(SegmentKind.SignOff, counterpartName: null));
            Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("music", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SignOnTemplateNamesTheCounterpartWhenPresent()
        {
            var text = renderer.Expand(HandoffRequest(SegmentKind.SignOn, "Daybreak Dana"));
            Assert.Contains("Daybreak Dana", text);
        }

        [Fact]
        public void SignOnTemplateStaysMusicOnlyWhenCounterpartAbsent()
        {
            var text = renderer.Expand(HandoffRequest(SegmentKind.SignOn, counterpartName: null));
            Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("music", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -----------------------------------------------------------------------
    // AC5 (F92.5) — blurb-cache posture for genuinely LLM-authored copy
    // -----------------------------------------------------------------------

    public sealed class ScenarioBlurbCachePosture : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        static TtsSegmentSource BuildSource(ISegmentCopyWriter copyWriter, FakeTtsSynthesizer synth, string cacheRoot) =>
            new(
                copyWriter,
                synth,
                new FakeLoudnessAnalyzer(),
                new FakeCueAnalyzer(),
                NoCorrections.Provider(),
                NoCorrections.PersonaCache(),
                NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(),
                NoCorrections.PersonaPaceCache(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                NullLogger<TtsSegmentSource>.Instance);

        [Fact]
        public async Task GenuineLlmAuthoredSignOffLandsInBlurbs()
        {
            var copyWriter = new FakeSegmentCopyWriter("Catch you later, Nite Owl's got the chair.", freshPerAiring: true);
            var source = BuildSource(copyWriter, synth, cacheRoot);

            var item = await source.RenderAsync(HandoffRequest(SegmentKind.SignOff, "Nite Owl"), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("blurbs", Path.GetFileName(Path.GetDirectoryName(item!.Locator)));
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // F92.4/F92.5 design ruling (T123 review finding): a handoff piece must NEVER air
    // non-LLM-authored copy. F92.4's ladder is "two-piece -> whichever piece rendered -> clean
    // cut" — there is no "templated piece" rung — and F92.5 states handoff pieces ARE LLM-authored
    // blurbs, full stop. Every writer-chain miss (LlmCopyWriter's own template-fallback degrade AND
    // DegradationGatedCopyWriter routing straight past it in Hard mode or off an unclaimed Soft
    // cadence slot) must render null rather than air, so the NEXT boundary gets to retry the full
    // ceremony (T124's producer treats a null piece as the drop signal F92.4 describes).
    // -----------------------------------------------------------------------

    public sealed class ScenarioNonLlmAuthoredCopyNeverAirs : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        static TtsSegmentSource BuildSource(
            ISegmentCopyWriter copyWriter, FakeTtsSynthesizer synth, string cacheRoot,
            ILogger<TtsSegmentSource>? logger = null) =>
            new(
                copyWriter,
                synth,
                new FakeLoudnessAnalyzer(),
                new FakeCueAnalyzer(),
                NoCorrections.Provider(),
                NoCorrections.PersonaCache(),
                NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(),
                NoCorrections.PersonaPaceCache(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                logger ?? NullLogger<TtsSegmentSource>.Instance);

        [Fact]
        public async Task TemplateFallbackSignOffRendersNull()
        {
            // F92.5: "never templated-cached" holds because a template-fallback miss on a handoff
            // never reaches the cache — or the air — at all (unlike LeadIn/BackAnnounce, whose own
            // template-fallback copy IS the ordinary forever-cache, see Story122_BlurbAudioGc.cs).
            var logger = new CapturingLogger<TtsSegmentSource>();
            var copyWriter = new FakeSegmentCopyWriter("That's me for now.", freshPerAiring: false);
            var source = BuildSource(copyWriter, synth, cacheRoot, logger);

            var item = await source.RenderAsync(HandoffRequest(SegmentKind.SignOff, counterpartName: null), CancellationToken.None);

            Assert.Null(item);
            Assert.Single(logger.Warnings);
        }

        [Fact]
        public async Task TemplateFallbackSignOnRendersNull()
        {
            var logger = new CapturingLogger<TtsSegmentSource>();
            var copyWriter = new FakeSegmentCopyWriter("Taking it from here.", freshPerAiring: false);
            var source = BuildSource(copyWriter, synth, cacheRoot, logger);

            var item = await source.RenderAsync(HandoffRequest(SegmentKind.SignOn, counterpartName: null), CancellationToken.None);

            Assert.Null(item);
            Assert.Single(logger.Warnings);
        }

        [Fact]
        public async Task HardDegradationModeHandoffRendersNull()
        {
            // Drives the real production chain — DegradationGatedCopyWriter wrapping a real
            // LlmCopyWriter and TemplateCopyWriter, the exact shape TtsServiceCollectionExtensions
            // wires behind ISegmentCopyWriter (Story188_LlmDegradationModes.cs's own BuildGatedWriter
            // idiom) — pinned to Hard: zero LLM calls (SPEC F69.1), so the gated writer routes
            // straight to TemplateCopyWriter's SignOn arm, bypassing LlmCopyWriter entirely. That
            // arm still renders correct text (kept for exactly this reason) — it just must never
            // reach air for a handoff kind.
            var controller = new DegradationController(
                new FakeDependencyHealth(),
                new LlmCopyStatusHolder(),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions
                {
                    Endpoint = "https://llm.example/v1",
                    DegradationPin = "hard",
                }),
                new TestOptionsMonitor<DegradationOptions>(new DegradationOptions()),
                TimeProvider.System,
                new CapturingLogger<DegradationController>());
            var template = new TemplateCopyWriter(new PatterTemplateRenderer());
            var llmWriter = new LlmCopyWriter(
                template,
                new FakeHttpClientFactory(),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "https://llm.example/v1" }),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                controller);
            var gated = new DegradationGatedCopyWriter(
                controller, llmWriter, template, new TestOptionsMonitor<DegradationOptions>(new DegradationOptions()),
                TimeProvider.System);
            var source = BuildSource(gated, synth, cacheRoot);

            var item = await source.RenderAsync(HandoffRequest(SegmentKind.SignOn, counterpartName: null), CancellationToken.None);

            Assert.Null(item);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // LlmCopyWriter routing — SignOff/SignOn are genuinely LLM-eligible kinds
    // (F92.5 "LLM-authored blurbs" only holds if the writer actually calls out)
    // -----------------------------------------------------------------------

    public sealed class ScenarioHandoffKindsGoToTheLlm : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        static LlmCopyWriter BuildWriter(string endpoint) =>
            new(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new FakeHttpClientFactory(),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions
                {
                    Endpoint = endpoint,
                    Model = "test-model",
                    TimeoutSeconds = 5,
                    MaxCopyChars = 450,
                }),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader());

        [Fact]
        public async Task SignOffCopyComesFromTheMockCompletionNotTheTemplate()
        {
            mock.ReplyContent = "Catch you on the flip side, Nite Owl's up next.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(HandoffRequest(SegmentKind.SignOff, "Nite Owl"), CancellationToken.None);

            Assert.Equal("Catch you on the flip side, Nite Owl's up next.", result.Text);
            Assert.True(result.FreshPerAiring);
            // The counterpart's name reached the wire, not just the mock's canned reply (T123
            // review finding) — proves the PROMPT carried the name, not merely a coincidence of the
            // reply text also containing "Nite Owl".
            Assert.Contains("Nite Owl", mock.Requests[0].Body);
        }

        [Fact]
        public async Task SignOnCopyComesFromTheMockCompletionNotTheTemplate()
        {
            mock.ReplyContent = "Morning, folks — Daybreak Dana just handed me the mic.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(HandoffRequest(SegmentKind.SignOn, "Daybreak Dana"), CancellationToken.None);

            Assert.Equal("Morning, folks — Daybreak Dana just handed me the mic.", result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Contains("Daybreak Dana", mock.Requests[0].Body);
        }
    }
}
