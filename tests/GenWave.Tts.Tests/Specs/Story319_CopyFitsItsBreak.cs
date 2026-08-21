// STORY-319 — Copy fits its break (gh-#277 · SPEC F123 · PLAN VQ-e, T262–T264)
//
// BDD specification — xUnit, pending until /build-loop turns them green. The measured root
// (design session 2026-08-12): the completion request carries NO generation cap at all, the
// only length control is a post-cleanup char reject, and a rejected SignOff/SignOn/
// ContextSegment airs SILENCE (their template rung deliberately drops). One assertion per
// Fact; happy path first and exhaustive; sad path segregated. The T265 wire acceptance
// (trimmed copy audible on a running stack, Trimmed in /api/llm-calls, the cap on the wire)
// is a production-binary check, deliberately not represented here.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureCopyFitsItsBreak
{
    // A reply whose FIRST sentence ("Great tune coming up.", 21 chars) comfortably fits under the
    // 40-char cap used throughout the trim scenarios below, but whose full text (120 chars) does
    // not — the exact shape F123.2's salvage exists for. Pinned as a constant so every scenario
    // that trims this same reply agrees on the expected cut, rather than three copies of the same
    // literal that could quietly drift apart.
    const string OverLengthReply =
        "Great tune coming up. Stick around for more good vibes tonight on the station, we have plenty more where that came from.";
    const string ExpectedTrim = "Great tune coming up.";
    const int TrimMaxCopyChars = 40;

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest SignOffRequest() =>
        new(SegmentKind.SignOff, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static LlmCopyWriter BuildWriter(string endpoint, int maxCopyChars) =>
        BuildWriterWithRingAndLogger(endpoint, maxCopyChars, persona: null).Writer;

    /// <summary>
    /// The one constructor arg list in this file — <see cref="BuildWriter"/> above is expressed in
    /// terms of this, not a second copy of it, so the two can never drift apart. Also hands back the
    /// ring and the capturing logger, which is what the F123.4 observability facts need to inspect
    /// (mirrors Story119_LlmCopyWriter's own BuildWriterWithRing idiom).
    /// </summary>
    static (LlmCopyWriter Writer, LlmCallRing Ring, CapturingLogger<LlmCopyWriter> Logger) BuildWriterWithRingAndLogger(
        string endpoint, int maxCopyChars, Persona? persona)
    {
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var logger = new CapturingLogger<LlmCopyWriter>();
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = maxCopyChars,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor { Persona = persona },
            logger,
            TimeProvider.System,
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader());
        return (writer, ring, logger);
    }

    static async Task<string> TemplateTextAsync(SegmentRequest request) =>
        (await new TemplateCopyWriter(new PatterTemplateRenderer()).WriteAsync(request, CancellationToken.None)).Text;

    static int ExtractMaxTokens(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("max_tokens").GetInt32();
    }

    /// <summary>
    /// Mirrors Story193_PersonaPromptAssemblyAndClock's own ExtractMessageContent idiom (one
    /// project over): the system prompt this writer actually sent, read back off the captured
    /// wire body rather than re-derived from <see cref="LlmPromptBuilder.BuildSystemPrompt"/>
    /// directly — the F123.5 fact below needs the ACTUAL outbound request LlmCopyWriter built
    /// with its own configured MaxCopyChars, not a second, hand-built call to the builder.
    /// </summary>
    static string ExtractSystemPrompt(string body)
    {
        using var doc = JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "system")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheRequestCarriesADerivedGenerationCap : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;
        int smallConfigMaxTokens;
        int largeConfigMaxTokens;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();

            // Given Llm:MaxCopyChars is configured — arranged at two different values so the
            // second Fact can show the cap tracks it rather than a second, independent setting.
            await BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 60)
                .WriteAsync(LeadInRequest(), CancellationToken.None);
            smallConfigMaxTokens = ExtractMaxTokens(mock.Requests[0].Body);

            await BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 900)
                .WriteAsync(LeadInRequest(), CancellationToken.None);
            largeConfigMaxTokens = ExtractMaxTokens(mock.Requests[1].Body);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void The_completion_request_body_carries_a_max_token_cap()
        {
            // When the copywriter builds a completion request
            // Then the body carries a max-token cap — today the body is {model, messages} only
            Assert.True(smallConfigMaxTokens > 0);
        }

        [Fact]
        public void The_cap_is_derived_from_MaxCopyChars_not_a_second_setting()
        {
            // One knob: changing MaxCopyChars changes the cap; no new LlmOptions field
            // is read for it.
            Assert.True(largeConfigMaxTokens > smallConfigMaxTokens);
        }
    }

    public sealed class ScenarioOverLengthCopyIsTrimmedAtASentence : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;
        SegmentCopy result = null!;
        string expectedTemplate = "";

        public async Task InitializeAsync()
        {
            // Given cleaned copy longer than MaxCopyChars whose first sentence fits...
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = OverLengthReply;
            var writer = BuildWriter(mock.BaseUri.ToString(), TrimMaxCopyChars);
            expectedTemplate = await TemplateTextAsync(LeadInRequest());

            // When the length gate runs...
            result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void The_copy_is_cut_at_the_last_complete_sentence_under_the_cap()
        {
            // Then the result ends exactly at the last complete sentence that fits the cap — never a
            // char-index truncation mid-sentence (F123.2).
            Assert.Equal(ExpectedTrim, result.Text);
        }

        [Fact]
        public void The_trimmed_copy_airs_rather_than_falling_back()
        {
            // The salvage returns real copy, not the template fallback's own text.
            Assert.NotEqual(expectedTemplate, result.Text);
        }

        [Fact]
        public void A_mid_sentence_cut_never_occurs()
        {
            // The cut point is a sentence boundary by construction, never a char index.
            Assert.EndsWith(".", result.Text, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAnAbbreviationBoundaryFallsThroughToTheEarlierValidOne : IAsyncLifetime
    {
        // The review's own "St. Vincent" shape (SPEC F123.2 review finding, gh-#277 follow-up): a
        // genuinely correct sentence boundary ("Great tune.") comes BEFORE a LATER "St." abbreviation
        // period that would also fit under the cap. The bug this guards against: scanning every
        // candidate left-to-right and keeping whichever fits LAST would let the abbreviation period
        // overwrite the earlier, correct one — airing "...from St." instead of "Great tune.".
        const string Reply = "Great tune. Here's a brand new one from St. Vincent tonight, don't miss it.";
        // Covers both "Great tune." (cuts at 11 chars) and "...from St." (cuts at 43 chars) but not
        // the full 75-char reply — the exact window where the bug used to bite.
        const int MaxCopyChars = 50;

        MockCompletionsServer mock = null!;
        SegmentCopy result = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = Reply;
            var writer = BuildWriter(mock.BaseUri.ToString(), MaxCopyChars);

            result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void The_cut_lands_at_the_earlier_valid_boundary_not_the_abbreviation_period()
        {
            Assert.Equal("Great tune.", result.Text);
        }
    }

    public sealed class ScenarioTrimmedPersonaCopyBeatsSilenceOnTheTemplatelessKinds : IAsyncLifetime
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = OverLengthReply;
        }

        public async Task DisposeAsync()
        {
            await mock.DisposeAsync();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }

        [Fact]
        public async Task An_over_length_SignOff_whose_first_sentence_fits_airs_trimmed_copy()
        {
            // Given the F123.3 consequence: previously this kind aired NOTHING at all
            // (TtsSegmentSource drops non-fresh copy for SignOff/SignOn/ContextSegment) — driving the
            // REAL LlmCopyWriter through the REAL TtsSegmentSource is what proves the salvage's
            // FreshPerAiring:true actually clears that drop guard, not just that CleanCopy salvages.
            var writer = BuildWriter(mock.BaseUri.ToString(), TrimMaxCopyChars);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                writer, synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(), opts,
                NullLogger<TtsSegmentSource>.Instance);

            // When the segment is rendered end-to-end (copywriter -> hygiene+salvage -> TtsSegmentSource's
            // own SignOff/SignOn/ContextSegment drop guard)...
            await source.RenderAsync(SignOffRequest(), CancellationToken.None);

            // Then the SALVAGED text reached the synthesizer — proving FreshPerAiring:true actually
            // cleared the drop guard AND that what airs is the trim, not merely that something did.
            Assert.Equal(ExpectedTrim, synth.LastText);
        }
    }

    public sealed class ScenarioATrimIsVisibleAsDisciplineNotOutage : IAsyncLifetime
    {
        // Both facts here share this ONE arrangement (persona: null) — the persona-carrying fact
        // needs a different persona argument and lives in its own scenario below instead
        // (ScenarioATrimsInformationLineNamesItsCallContext) rather than mixing two arranges under
        // one IAsyncLifetime.
        MockCompletionsServer mock = null!;
        LlmCallRing ring = null!;
        CapturingLogger<LlmCopyWriter> logger = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = OverLengthReply;
            var (writer, builtRing, builtLogger) =
                BuildWriterWithRingAndLogger(mock.BaseUri.ToString(), TrimMaxCopyChars, persona: null);
            ring = builtRing;
            logger = builtLogger;

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void The_status_ring_outcome_is_Trimmed_not_Failed()
        {
            // The call inspector ring (gh-#277's own diagnostic surface) shows Trimmed, not the
            // generic Ok/Failed a pre-F123 caller would only ever have seen.
            Assert.Equal(LlmCallOutcome.Trimmed, Assert.Single(ring.Snapshot()).Outcome);
        }

        [Fact]
        public void The_trim_line_never_reaches_Warnings()
        {
            // Information-not-WARN IS the point (F123.4): a trim is discipline, not an outage, so it
            // must never promote to CapturingLogger's Warning-and-above floor.
            Assert.Empty(logger.Warnings);
        }
    }

    public sealed class ScenarioATrimsInformationLineNamesItsCallContext : IAsyncLifetime
    {
        static readonly Persona TestPersona = new(3, "Neon Nightowl", "Spins vinyl til dawn.",
            "moody, late-night", "af_sky", DateTime.UtcNow, DateTime.UtcNow);

        MockCompletionsServer mock = null!;
        CapturingLogger<LlmCopyWriter> logger = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = OverLengthReply;
            var (writer, _, builtLogger) =
                BuildWriterWithRingAndLogger(mock.BaseUri.ToString(), TrimMaxCopyChars, TestPersona);
            logger = builtLogger;

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void One_information_line_names_kind_persona_and_chars_before_after()
        {
            // Exactly one matching line, not merely at-least-one (F123.4's "ONE information line").
            Assert.Single(logger.Messages, m =>
                m.Contains("LeadIn", StringComparison.Ordinal) &&
                m.Contains("Neon Nightowl", StringComparison.Ordinal) &&
                m.Contains(OverLengthReply.Length.ToString(), StringComparison.Ordinal) &&
                m.Contains(ExpectedTrim.Length.ToString(), StringComparison.Ordinal));
        }
    }

    public static class ScenarioThePromptStatesTheWordBudget
    {
        [Fact]
        public static async Task The_length_instruction_carries_a_numeric_word_figure()
        {
            // Given a configured Llm:MaxCopyChars (300 chars / LlmPromptBuilder's own 6
            // chars-per-word divisor = 50 words) and a real on-air render...
            await using var mock = await MockCompletionsServer.StartAsync();
            await BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 300)
                .WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the length instruction carries that derived word figure — stated, not
            // enforced; T262's max_tokens cap is the enforcement, T263's sentence-trim salvage
            // the backstop.
            var systemPrompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains("~50 words", systemPrompt, StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioNoCompleteSentenceFits : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task Copy_whose_first_sentence_exceeds_the_cap_is_rejected_as_today()
        {
            // Given cleaned copy whose FIRST sentence already exceeds MaxCopyChars — one long
            // sentence, no earlier terminator for the salvage to cut at...
            mock.ReplyContent = "This completion is intentionally far longer than the configured limit.";
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var writer = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 10);

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // Then the pre-F123 posture stands: null → template (or the templateless drop) — nothing
            // complete fits under the cap, so there is nothing for F123.2 to salvage.
            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public async Task A_lone_initial_period_is_never_treated_as_a_boundary()
        {
            // Given the only period under the cap belongs to a lone initial ("J.") — a real DJ
            // announcing "J. Cole" mid-sentence, never a sentence's own end (SPEC F123.2 review
            // finding) — so nothing complete survives and the pre-F123 reject stands.
            mock.ReplyContent = "J. Cole is spinning a brand new record for the whole crew tonight.";
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var writer = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 5);

            var result = await writer.WriteAsync(request, CancellationToken.None);

            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public async Task Text_whose_only_boundary_under_the_cap_is_an_abbreviation_period_is_rejected()
        {
            // Given the only period under the cap belongs to a known abbreviation ("Dr.") — never a
            // sentence's own end (SPEC F123.2 review finding) — so nothing complete survives and the
            // pre-F123 reject stands, exactly like the no-terminator-at-all case above.
            mock.ReplyContent = "Dr. Dre is spinning a brand new record for the whole crew tonight.";
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var writer = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 6);

            var result = await writer.WriteAsync(request, CancellationToken.None);

            Assert.Equal(expected, result.Text);
        }
    }

    public static class ScenarioADegenerateCapNeverPoisonsTheRequest
    {
        [Fact]
        public static async Task A_tiny_MaxCopyChars_clamps_the_derived_cap_to_a_stated_floor()
        {
            // Given a MaxCopyChars so small the derived token cap would be nonsensical
            await using var mock = await MockCompletionsServer.StartAsync();
            await BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 1)
                .WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the cap clamps and the request remains valid — 16 pins the shipped floor value
            // deliberately (not a re-derivation of it), so a future edit that quietly zeroes the
            // floor const still fails this fact instead of passing 0 == 0.
            Assert.Equal(16, ExtractMaxTokens(mock.Requests[0].Body));
        }
    }
}
