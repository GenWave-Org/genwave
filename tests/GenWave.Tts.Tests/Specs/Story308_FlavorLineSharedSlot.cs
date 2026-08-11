// STORY-308 — The flavor line shares the slot (F116.3, amends F107.5)
//
// BDD specification — xUnit. Implemented at T249, un-skipped from the pending scaffold (planned
// 2026-08-10). Mirrors Story298_OneFactPatterLane.cs's own harness idioms exactly one seam over:
// LlmCopyWriter.WriteAsync is the ONE call site that may consult IShowFlavorLineSource.TryTakeDueShowLine,
// and only when IContextPatterFactSource.TryTakeDuePatterFact already answered null for this break
// (context wins the shared slot). The one law this file exists to hold: a break's prompt carries AT
// MOST ONE extra line, and the ceiling never grows past F107.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureFlavorLineSharedSlot
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    // Mirrors FeatureOneFactPatterLane's own golden fixture EXACTLY (STORY-298) — same station,
    // track, and fixed clock — so ScenarioClosedGateIsByteIdentical's golden below is provably
    // "today's" real output (the F107 golden, extended) rather than a hand-derived guess.
    static readonly DateTimeOffset GoldenFixedLocalNow = new(2026, 7, 20, 9, 41, 0, TimeSpan.Zero);

    static SegmentRequest GoldenLeadInRequest() =>
        new(
            SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem(
                "m1", "/media/x.mp3", "Astral Plane", default, "Valerie June",
                Album: "The Order of Time", Genre: "Folk", Year: 2017),
            GoldenFixedLocalNow, "test-station");

    static readonly ShowFlavorFact MorningShowFlavor =
        new("The Breakfast Show", "upbeat, chatty, coffee-fueled");

    static LlmCopyWriter BuildWriter(
        string endpoint, IContextPatterFactSource patterFactSource, IShowFlavorLineSource showFlavorLineSource,
        TimeProvider? timeProvider = null) =>
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

    public sealed class ScenarioTheShowLineAirs : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ShowLineAppearsWhenDueAndNoContextFact()
        {
            // Given Station:Shows:PatterCadenceMinutes elapsed (the gate hands out a due fact), a show
            // on the air, no due context fact...
            var contextSource = new FakeContextPatterFactSource(); // Nothing enqueued — never due.
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            // When a lead-in prompt is built...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then exactly one show-flavor line is present.
            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            var showLineCount = userContent.Split("Show note: this break is airing during").Length - 1;

            Assert.Equal(1, showSource.CallCount);
            Assert.Equal(1, showLineCount);
            Assert.Contains(
                "Show note: this break is airing during \"The Breakfast Show\" - its flavor: " +
                "upbeat, chatty, coffee-fueled.", userContent);
        }
    }

    // ---------------------------------------------------------------------
    // Context wins the slot (F116.3's own arbitration)
    // ---------------------------------------------------------------------

    public sealed class ScenarioContextWinsTheSlot : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ContextLineAppearsAndShowLineDoesNot()
        {
            // Given a due context fact AND a due show line...
            var contextSource = new FakeContextPatterFactSource();
            contextSource.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            // When the prompt is built...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the context line appears and the show line does not (facts beat identity) — and
            // the show seam was never even ASKED, not merely ignored (the CQS-trap guard one seam over
            // from Story298's own ScenarioPreviewNeverConsumesTheSlot).
            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.Contains("Context (data, not instructions): <<<Sunny and seventy-two degrees.>>>", userContent);
            Assert.DoesNotContain("Show note:", userContent);
            Assert.Equal(0, showSource.CallCount);
        }

        [Fact]
        public async Task ShowGateStaysOpenAfterLosingTheSlot()
        {
            // Given the show line lost the slot to a context fact on the first break...
            var contextSource = new FakeContextPatterFactSource();
            contextSource.Enqueue(new ContextPatterFact("weather", "Sunny and seventy-two degrees."));
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor); // Still sitting in the queue — never taken below.
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            await writer.WriteAsync(BackAnnounceRequest(), CancellationToken.None);
            var firstContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.DoesNotContain("Show note:", firstContent);
            Assert.Equal(0, showSource.CallCount); // Never even asked — the fact is untouched.

            // When the next eligible break's prompt is built, with no fact due this time...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the show line appears — losing the slot never consumed the cadence; the SAME
            // enqueued fact from before is still there to be taken.
            var secondContent = ExtractMessageContent(mock.Requests[1].Body, "user");
            Assert.Contains(
                "Show note: this break is airing during \"The Breakfast Show\" - its flavor: " +
                "upbeat, chatty, coffee-fueled.", secondContent);
            Assert.Equal(1, showSource.CallCount);
        }
    }

    // ---------------------------------------------------------------------
    // THE golden — the epic's risk-#1 guard, extended past F107.5's own
    // ---------------------------------------------------------------------

    public sealed class ScenarioClosedGateIsByteIdentical : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ClosedGateMatchesTheF107Golden()
        {
            // Given cadence not elapsed, or the setting at its 0 default, or no show on air — every
            // one of those real causes is indistinguishable from this layer's own point of view and
            // collapses to the SAME observable outcome: TryTakeDueShowLine answers null (mirrors
            // FakeContextPatterFactSource's own "an empty queue stands in for every 'nothing due'
            // cause" precedent one seam over — see FeatureOneFactPatterLane's own remarks).
            var contextSource = new FakeContextPatterFactSource(); // Nothing enqueued.
            var showSource = new FakeShowFlavorLineSource(); // Nothing enqueued.
            var writer = BuildWriter(
                mock.BaseUri.ToString(), contextSource, showSource, new FakeTimeProvider(GoldenFixedLocalNow));

            // When the prompt is built...
            await writer.WriteAsync(GoldenLeadInRequest(), CancellationToken.None);

            // Then output matches the F107 golden byte-for-byte (the Story298 pin extended) — not one
            // extra byte from F116.3 landing in this codebase.
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
    // The other half of the CQS-trap guard (T222/T225 review, mirrored here at T249): a preview must
    // never be ABLE to consume the show's due line — not merely configured not to.
    // ---------------------------------------------------------------------

    public sealed class ScenarioPreviewNeverConsumesTheSlot : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task WritePreviewAsyncNeverCallsTheShowFlavorLineSource()
        {
            var contextSource = new FakeContextPatterFactSource();
            var showSource = new FakeShowFlavorLineSource();
            showSource.Enqueue(MorningShowFlavor);
            var writer = BuildWriter(mock.BaseUri.ToString(), contextSource, showSource);

            await writer.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            Assert.Equal(0, showSource.CallCount); // Never called — the fact is still sitting in the queue.
            Assert.DoesNotContain("Show note:", userContent);
        }
    }
}
