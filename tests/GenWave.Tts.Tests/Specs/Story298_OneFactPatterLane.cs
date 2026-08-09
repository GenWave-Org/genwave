// STORY-298 — One fact, sometimes, in the patter (F107.5)
//
// BDD specification — xUnit. LlmCopyWriter.WriteAsync (on-air only) is the ONE call site that may
// pull IContextPatterFactSource.TryTakeDuePatterFact() and pass the result into
// LlmPromptBuilder.BuildUserContent; WritePreviewAsync never touches the seam at all. The pipeline's
// own cadence/freshness mechanics (what makes a fact "due" or "stale" in the first place) are pinned
// in GenWave.Context.Tests/Specs/Story296_ContextPipeline.cs — this file proves the Tts-layer half:
// the take happens exactly once, exactly where it should, and the prompt it produces is either
// byte-identical to before F107 (no fact) or carries exactly one compact line (a fact).

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureOneFactPatterLane
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    // Mirrors FeatureTasteBecomesAudible's own fixture EXACTLY (STORY-214) — same station, track,
    // and fixed clock — so ScenarioOtherwiseByteIdentical's golden below is provably "today's" real
    // output (copied from an already-passing, independently-authored pin) rather than a hand-derived
    // guess this same change could have gotten subtly wrong.
    static readonly DateTimeOffset GoldenFixedLocalNow = new(2026, 7, 20, 9, 41, 0, TimeSpan.Zero);

    static SegmentRequest GoldenLeadInRequest() =>
        new(
            SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem(
                "m1", "/media/x.mp3", "Astral Plane", default, "Valerie June",
                Album: "The Order of Time", Genre: "Folk", Year: 2017),
            GoldenFixedLocalNow, "test-station");

    static LlmCopyWriter BuildWriter(
        string endpoint, IContextPatterFactSource patterFactSource, TimeProvider? timeProvider = null) =>
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
            timeProvider ?? TimeProvider.System,
            new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
            new FakeDegradationModeReader(),
            stationClock: null,
            patterFactSource: patterFactSource);

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

    public sealed class ScenarioADueFactAppearsOnce : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ExactlyOneContextLineIsPresentWhenAFactIsDue()
        {
            // Two facts queued — mirrors two enabled providers both having something ready this
            // slot. ContextPipeline.TryTakeDuePatterFact's own contract already guarantees at most
            // one is EVER handed out per call (pinned in GenWave.Context.Tests); this fact instead
            // pins the OTHER half of "at most one": LlmCopyWriter.WriteAsync calls TryTake exactly
            // once, so however many facts a real pipeline might have queued up, only one ever reaches
            // the prompt — a second due provider does NOT add a second line.
            var source = new FakeContextPatterFactSource();
            source.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            source.Enqueue(new ContextPatterFact("history", "On this day in 1969 the first ATM opened."));
            var writer = BuildWriter(mock.BaseUri.ToString(), source);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            var contextLineCount = userContent.Split("Context:").Length - 1;

            Assert.Equal(1, source.CallCount); // The take happened exactly once.
            Assert.Equal(1, contextLineCount); // ...and exactly one line reached the prompt.
            Assert.Contains("Context: Sunny and seventy-two degrees.", userContent);
            Assert.DoesNotContain("first ATM opened", userContent); // The second fact was never taken.
        }
    }

    // ---------------------------------------------------------------------
    // THE golden — the epic's risk-#1 guard
    // ---------------------------------------------------------------------

    public sealed class ScenarioOtherwiseByteIdentical : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task NoDueFactMeansTheGoldenPromptByteForByte()
        {
            // A fake source with nothing enqueued (no fact due) must produce the exact same prompt
            // bytes LlmCopyWriter produced before F107 ever touched this file. Expected below is not
            // hand-derived — it is copied verbatim from FeatureTasteBecomesAudible's own
            // ThePromptMatchesPreF82BehaviorByteForByte (STORY-214, Story214_TasteBecomesAudible.cs),
            // an already-passing, independently-authored pin of this SAME station/track/clock
            // fixture's real output — a REAL byte golden, not a guess.
            var source = new FakeContextPatterFactSource();
            var writer = BuildWriter(mock.BaseUri.ToString(), source, new FakeTimeProvider(GoldenFixedLocalNow));

            await writer.WriteAsync(GoldenLeadInRequest(), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            const string Expected =
                "Station: GenWave\n" +
                "Local time: 2026-07-20 09:41\n" +
                "Current date/time (station-local): Monday, July 20, 2026, 9:41 AM\n" +
                "Segment: lead-in - the track below is about to play next. Announce it as upcoming.\n" +
                "Title: Astral Plane\n" +
                "Artist: Valerie June\n" +
                "Album: The Order of Time\n" +
                "Genre: Folk\n" +
                "Year: 2017";

            Assert.Equal(Expected, userContent);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioStaleFactsNeverSpeak : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task AFactPastFreshUntilProducesNoLine()
        {
            // TryTakeDuePatterFact's own contract (IContextPatterFactSource's remarks) makes "stale"
            // and "nothing was ever due" the SAME observable outcome at this seam — null — so there is
            // no second signal here for a fake to distinguish WHY. The mechanics that actually compute
            // staleness (a cached fetch outliving its own ContextContent.FreshUntil mid-slot) live
            // entirely inside GenWave.Context.ContextPipeline and are pinned THERE:
            // FeatureContextPipeline.ScenarioSkipNeverSilence.StaleContentIsNeverServed
            // (GenWave.Context.Tests/Specs/Story296_ContextPipeline.cs) asserts TryTakeDuePatterFact()
            // itself returns null once FreshUntil elapses. What this fact pins is the other half of
            // the same guarantee, honestly reduced to what this layer can actually observe: given that
            // null (however it arose), the copywriter never speaks a stale fact — no "Context:" line
            // reaches the wire, for either LLM-eligible kind (proven here via BackAnnounce, the
            // LeadIn case already being exercised by the golden above).
            var source = new FakeContextPatterFactSource(); // Nothing enqueued — TryTake always null.
            var writer = BuildWriter(mock.BaseUri.ToString(), source);

            await writer.WriteAsync(BackAnnounceRequest(), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.DoesNotContain("Context:", userContent);
        }
    }

    // ---------------------------------------------------------------------
    // The other half of the CQS-trap guard (T222 review): a preview must never be ABLE to consume
    // the break's one due fact — not merely configured not to.
    // ---------------------------------------------------------------------

    public sealed class ScenarioPreviewNeverConsumesTheSlot : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task WritePreviewAsyncNeverCallsThePatterFactSource()
        {
            var source = new FakeContextPatterFactSource();
            source.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            var writer = BuildWriter(mock.BaseUri.ToString(), source);

            await writer.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.Equal(0, source.CallCount); // Never called — the fact is still sitting in the queue.
            Assert.DoesNotContain("Context:", userContent);
        }
    }
}
