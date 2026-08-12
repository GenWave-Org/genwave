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
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureCopyFitsItsBreak
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static LlmCopyWriter BuildWriter(string endpoint, int maxCopyChars) =>
        new(
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
            new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>(),
            TimeProvider.System,
            new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
            new FakeDegradationModeReader());

    static int ExtractMaxTokens(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("max_tokens").GetInt32();
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

    public static class ScenarioOverLengthCopyIsTrimmedAtASentence
    {
        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void The_copy_is_cut_at_the_last_complete_sentence_under_the_cap()
        {
            // Given cleaned copy longer than MaxCopyChars whose first sentence fits
            // When  the length gate runs
            // Then  the result ends exactly at a sentence terminator and fits the cap
            Assert.Fail("pending T263");
        }

        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void The_trimmed_copy_airs_rather_than_falling_back()
        {
            // Then the salvage returns copy — the template fallback is not consulted
            Assert.Fail("pending T263");
        }

        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void A_mid_sentence_cut_never_occurs()
        {
            // The cut point is a sentence boundary by construction, never a char index.
            Assert.Fail("pending T263");
        }
    }

    public static class ScenarioTrimmedPersonaCopyBeatsSilenceOnTheTemplatelessKinds
    {
        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void An_over_length_SignOff_whose_first_sentence_fits_airs_trimmed_copy()
        {
            // Given the F123.3 consequence: previously this kind aired NOTHING at all
            // (TtsSegmentSource drops non-fresh copy for SignOff/SignOn/ContextSegment)
            Assert.Fail("pending T263");
        }
    }

    public static class ScenarioATrimIsVisibleAsDisciplineNotOutage
    {
        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void The_status_ring_outcome_is_Trimmed_not_Failed()
        {
            Assert.Fail("pending T263");
        }

        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void One_information_line_names_kind_persona_and_chars_before_after()
        {
            Assert.Fail("pending T263");
        }
    }

    public static class ScenarioThePromptStatesTheWordBudget
    {
        [Fact(Skip = "Pending T264 — see docs/PLAN.md")]
        public static void The_length_instruction_carries_a_numeric_word_figure()
        {
            // Given the system prompt is built
            // Then the instruction is quantified ("at most ~N words"), derived from the
            // same MaxCopyChars — stated, not enforced; T262's cap is the enforcement.
            Assert.Fail("pending T264");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioNoCompleteSentenceFits
    {
        [Fact(Skip = "Pending T263 — see docs/PLAN.md")]
        public static void Copy_whose_first_sentence_exceeds_the_cap_is_rejected_as_today()
        {
            // Given cleaned copy whose FIRST sentence already exceeds MaxCopyChars
            // Then the pre-F123 posture stands: null → template (or the templateless drop)
            Assert.Fail("pending T263");
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
