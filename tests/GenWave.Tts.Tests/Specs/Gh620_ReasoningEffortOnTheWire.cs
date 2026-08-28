// gh-#620 — Thinking-capable models return empty copy via /v1/chat/completions
//
// BDD specification — xUnit. The measured root (bench morning 2026-08-24): gemma4:12b put its
// chain-of-thought in message.reasoning, spent the whole max_tokens budget there, and returned
// content "" on 13/13 calls — every break templated, the tile flapping, the WARN pointing at the
// wrong levers. Two writers post completions from this project; both are pinned here against the
// captured request (MockCompletionsServer), and the copy writer's fallback WARN is pinned to name
// a reasoning-only reply for what it is. Companion files: Core.Tests owns the vocabulary,
// MediaLibrary.Tests the mood/explicit posters, Host.Tests the setting + the wish parser.
namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureReasoningEffortOnTheWire
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static PersonaCard MakeCard(string name, string soul) =>
        new(PersonaCard.CurrentSchemaVersion, name, Tagline: "", soul, Quirks: [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), EnergyDisposition: 0, Lore: [], Corrections: []);

    static CrosstalkExchangeRequest CrosstalkRequest() =>
        new(MakeCard("Neon Nightowl", "Neon Nightowl spins moody late-night sets."),
            MakeCard("Daybreak Dana", "Daybreak Dana brings bright morning energy."),
            "GenWave", ShowName: "Night Shift", Daypart: "late night", StationLocalNow: DateTimeOffset.UtcNow);

    static LlmOptions Options(string endpoint, string reasoningEffort) => new()
    {
        Endpoint = endpoint,
        Model = "test-model",
        TimeoutSeconds = 5,
        MaxCopyChars = 450,
        ReasoningEffort = reasoningEffort,
    };

    static (LlmCopyWriter Writer, LlmCallRing Ring, CapturingLogger<LlmCopyWriter> Logger) BuildCopyWriter(
        string endpoint, string reasoningEffort)
    {
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var logger = new CapturingLogger<LlmCopyWriter>();
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(Options(endpoint, reasoningEffort)),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            logger,
            TimeProvider.System,
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader());
        return (writer, ring, logger);
    }

    static CrosstalkScriptWriter BuildCrosstalkWriter(string endpoint, string reasoningEffort) =>
        new(
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(Options(endpoint, reasoningEffort)),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions { DurationTargetSeconds = 25 }),
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new CapturingLogger<CrosstalkScriptWriter>(),
            TimeProvider.System);

    static string? ReasoningEffortOf(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("reasoning_effort", out var value) ? value.GetString() : null;
    }

    static bool CarriesReasoningEffort(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("reasoning_effort", out _);
    }

    static async Task<string> TemplateTextAsync(SegmentRequest request) =>
        (await new TemplateCopyWriter(new PatterTemplateRenderer()).WriteAsync(request, CancellationToken.None)).Text;

    // ---------------------------------------------------------------------
    // HAPPY PATH — the copy writer's request
    // ---------------------------------------------------------------------

    public sealed class ScenarioCopyWriterRequest
    {
        [Fact]
        public async Task The_default_request_carries_reasoning_effort_none()
        {
            await using var mock = await MockCompletionsServer.StartAsync();
            var (writer, _, _) = BuildCopyWriter(mock.BaseUri.ToString(), new LlmOptions().ReasoningEffort);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("none", ReasoningEffortOf(Assert.Single(mock.Requests).Body));
        }

        [Fact]
        public async Task An_effort_level_is_sent_lowercased()
        {
            await using var mock = await MockCompletionsServer.StartAsync();
            var (writer, _, _) = BuildCopyWriter(mock.BaseUri.ToString(), "High");

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("high", ReasoningEffortOf(Assert.Single(mock.Requests).Body));
        }

        [Fact]
        public async Task Omit_leaves_the_field_out_of_the_request_entirely()
        {
            await using var mock = await MockCompletionsServer.StartAsync();
            var (writer, _, _) = BuildCopyWriter(mock.BaseUri.ToString(), "omit");

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.False(CarriesReasoningEffort(Assert.Single(mock.Requests).Body));
        }

        [Fact]
        public async Task Omit_keeps_the_rest_of_the_request_shape_intact()
        {
            // "omit" must reproduce the pre-#620 body for a backend that rejects the field — the
            // null-omitting serializer must not have eaten anything else.
            await using var mock = await MockCompletionsServer.StartAsync();
            var (writer, _, _) = BuildCopyWriter(mock.BaseUri.ToString(), "omit");

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            using var doc = JsonDocument.Parse(Assert.Single(mock.Requests).Body);
            Assert.Equal("test-model", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
            Assert.True(doc.RootElement.GetProperty("max_tokens").GetInt32() > 0);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the crosstalk writer's request (the second poster in this project)
    // ---------------------------------------------------------------------

    public sealed class ScenarioCrosstalkWriterRequest
    {
        [Fact]
        public async Task The_default_request_carries_reasoning_effort_none()
        {
            await using var mock = await MockCompletionsServer.StartAsync();
            var writer = BuildCrosstalkWriter(mock.BaseUri.ToString(), new LlmOptions().ReasoningEffort);

            await writer.WriteExchangeAsync(CrosstalkRequest(), CancellationToken.None);

            Assert.Equal("none", ReasoningEffortOf(Assert.Single(mock.Requests).Body));
        }

        [Fact]
        public async Task Omit_leaves_the_field_out_of_the_request_entirely()
        {
            await using var mock = await MockCompletionsServer.StartAsync();
            var writer = BuildCrosstalkWriter(mock.BaseUri.ToString(), "omit");

            await writer.WriteExchangeAsync(CrosstalkRequest(), CancellationToken.None);

            Assert.False(CarriesReasoningEffort(Assert.Single(mock.Requests).Body));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a reasoning-only reply (the #620 failure shape, with the control set wrong)
    // ---------------------------------------------------------------------

    public sealed class ScenarioReasoningOnlyReply : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;
        LlmCallRing ring = null!;
        CapturingLogger<LlmCopyWriter> logger = null!;
        string resultText = "";
        string expectedTemplate = "";

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            // The wire shape Ollama served on 2026-08-24: no answer, chain-of-thought present, cut
            // at the cap. An operator who set "high" on a fenced budget lands exactly here.
            mock.ReplyContent = "";
            mock.ReplyReasoning = "Okay, the user wants a lead-in for Astral Plane by Valerie June. Let me think about";
            mock.ReplyFinishReason = "length";
            (var writer, ring, logger) = BuildCopyWriter(mock.BaseUri.ToString(), "high");

            expectedTemplate = await TemplateTextAsync(LeadInRequest());
            resultText = (await writer.WriteAsync(LeadInRequest(), CancellationToken.None)).Text;
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void The_copy_falls_back_to_the_template()
        {
            Assert.Equal(expectedTemplate, resultText);
        }

        [Fact]
        public void The_ring_records_it_as_an_empty_completion_the_taxonomy_is_unchanged()
        {
            Assert.Equal(LlmCallCause.EmptyCompletion, Assert.Single(ring.Snapshot()).Cause);
        }

        [Fact]
        public void The_warn_names_the_reasoning_not_the_wrong_levers()
        {
            var warn = Assert.Single(logger.Warnings);

            Assert.Contains("chars of reasoning", warn);
            Assert.Contains("finish_reason: length", warn);
            Assert.Contains("Llm:ReasoningEffort is 'high'", warn);
            Assert.DoesNotContain("empty after cleanup", warn);
        }
    }

    public sealed class ScenarioPlainEmptyReplyIsUnchanged
    {
        [Fact]
        public async Task An_empty_reply_with_no_reasoning_still_reads_empty_after_cleanup()
        {
            // The split must be real: a model that simply said nothing keeps the hygiene wording.
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "";
            var (writer, _, logger) = BuildCopyWriter(mock.BaseUri.ToString(), new LlmOptions().ReasoningEffort);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Contains("empty after cleanup", Assert.Single(logger.Warnings));
        }
    }
}
