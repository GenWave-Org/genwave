// STORY-329 — Crosstalk supersedes the gated lanes (SPEC F127.9, PLAN T287)
//
// BDD specification — xUnit. Mirrors Story298_OneFactPatterLane.cs/Story308_FlavorLineSharedSlot.cs's
// own harness idioms exactly two seams over: LlmCopyWriter.WriteAsync is the one call site that may
// consult IContextPatterFactSource.TryTakeDuePatterFact/IShowFlavorLineSource.TryTakeDueShowLine, and
// SegmentRequest.CrosstalkAiredThisBreak (the ONE new signal Orchestrator.EnqueuePatterAsync's own
// crosstalk vend step stamps, PLAN T287 — proven from the Orchestrator side in
// GenWave.Orchestration.Tests/Specs/Story329_BanterOnTheAir.cs, which cannot reach this writer at all)
// gates BOTH at once. The law this file exists to hold: a break vending crosstalk never even ASKS
// either seam — not "asks and discards" — so a lost slot costs neither lane its own cadence window
// (the identical CQS-trap guard Story298's ScenarioPreviewNeverConsumesTheSlot/Story308's own sibling
// already established one seam over).

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureCrosstalkSupersedesTheGatedLanes
{
    static SegmentRequest LeadInRequest(bool crosstalkAiredThisBreak) =>
        new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station")
        { CrosstalkAiredThisBreak = crosstalkAiredThisBreak };

    static SegmentRequest BackAnnounceRequest(bool crosstalkAiredThisBreak) =>
        new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station")
        { CrosstalkAiredThisBreak = crosstalkAiredThisBreak };

    static readonly ShowFlavorFact MorningShowFlavor =
        new("The Breakfast Show", "upbeat, chatty, coffee-fueled");

    static LlmCopyWriter BuildWriter(
        string endpoint, IContextPatterFactSource patterFactSource, IShowFlavorLineSource showFlavorLineSource) =>
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
            new FakeDegradationModeReader(),
            stationClock: null,
            patterFactSource: patterFactSource,
            showFlavorLineSource: showFlavorLineSource);

    static string ExtractMessageContent(string body, string role)
    {
        using var doc = JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == role)
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioABreakVendingCrosstalkAsksNeitherSeam : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task NeitherTheContextFactNorTheShowFlavorLineIsEverAsked()
        {
            // Given a due context fact AND a due show line — both would otherwise appear (proven by
            // Story298/Story308's own happy paths) — for a LeadIn request built for a break that is
            // ALSO vending crosstalk this SAME break
            var contextSource = new FakeContextPatterFactSource();
            contextSource.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            // When the prompt is built...
            await writer.WriteAsync(LeadInRequest(crosstalkAiredThisBreak: true), CancellationToken.None);

            // Then neither seam was ever called — not "called and discarded" — so both facts are still
            // sitting in their queues, untouched, for the NEXT break that does not vend crosstalk
            Assert.Equal(0, contextSource.CallCount);
            Assert.Equal(0, showSource.CallCount);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.DoesNotContain("Context (data, not instructions):", userContent);
            Assert.DoesNotContain("Show note:", userContent);
        }

        [Fact]
        public async Task TheSameHoldsForABackAnnounceRequest()
        {
            // Given the SAME due facts, for the OTHER music-adjacent kind a patter fact/flavor line
            // is meant to season (LlmPromptBuilder.IsPatterFactKind's own two-kind set)
            var contextSource = new FakeContextPatterFactSource();
            contextSource.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            await writer.WriteAsync(BackAnnounceRequest(crosstalkAiredThisBreak: true), CancellationToken.None);

            Assert.Equal(0, contextSource.CallCount);
            Assert.Equal(0, showSource.CallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the control: false changes nothing (byte-identical to pre-F127)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnOrdinaryBreakIsUntouched : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task WithCrosstalkAiredThisBreakFalseTheShowLineStillAppears()
        {
            // Given the SAME due show line, for an ORDINARY break (CrosstalkAiredThisBreak: false —
            // every pre-F127 caller's shape, and every F127 caller on a break that vends nothing)
            var contextSource = new FakeContextPatterFactSource();
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            await writer.WriteAsync(LeadInRequest(crosstalkAiredThisBreak: false), CancellationToken.None);

            // Then the show line still airs — F127.9's supersede is scoped to the SPECIFIC break that
            // actually vends crosstalk, never a station-wide change once the feature is enabled
            Assert.Equal(1, showSource.CallCount);
            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.Contains("Show note:", userContent);
        }
    }
}
