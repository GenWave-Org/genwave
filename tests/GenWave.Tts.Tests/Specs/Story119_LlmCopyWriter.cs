// STORY-119 — LLM writes lead-ins and back-announces, template on any miss
//
// BDD specification — xUnit. LlmCopyWriter against a local mock OpenAI-compatible
// completions server (operator ruling 2026-07-13: mocks in tests; real-LLM quality is
// the operator's listening test). The fallback ladder extends F12.4 — any miss yields
// template copy, never a stall.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureLlmCopyWriter
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest TimeDateRequest() =>
        new(SegmentKind.TimeDate, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static (LlmCopyWriter Writer, LlmCopyStatusHolder Holder, CapturingLogger<LlmCopyWriter> Logger) BuildWriter(
        string endpoint, int timeoutSeconds = 5, int maxCopyChars = 450)
    {
        var holder = new LlmCopyStatusHolder();
        var logger = new CapturingLogger<LlmCopyWriter>();
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = timeoutSeconds,
                MaxCopyChars = maxCopyChars,
            }),
            holder,
            new FakeActivePersonaAccessor(),
            logger,
            TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader());
        return (writer, holder, logger);
    }

    static async Task<string> TemplateTextAsync(SegmentRequest request) =>
        (await new TemplateCopyWriter(new PatterTemplateRenderer()).WriteAsync(request, CancellationToken.None)).Text;

    // ---------------------------------------------------------------------
    // HAPPY PATH — the LLM authors exactly the two track-anchored kinds
    // ---------------------------------------------------------------------

    public sealed class ScenarioBlurbKindsGoToTheLlm : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task LeadInCopyComesFromTheMockCompletion()
        {
            mock.ReplyContent = "Spinning up something great, stick around.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Llm:Endpoint set → LeadIn text is the mock's copy (cleaned), not the template (F34.2, AC1).
            Assert.Equal(mock.ReplyContent, result.Text);
        }

        [Fact]
        public async Task BackAnnounceCopyComesFromTheMockCompletion()
        {
            mock.ReplyContent = "That one's a classic, hope you enjoyed it.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(BackAnnounceRequest(), CancellationToken.None);

            // (F34.2, AC1).
            Assert.Equal(mock.ReplyContent, result.Text);
        }
    }

    public sealed class ScenarioTemplatedKindsNeverTouchTheLlm : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task StationIdUsesTheTemplateAndSendsNoRequest()
        {
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(StationIdRequest(), CancellationToken.None);

            // Mock's request log stays empty for StationId (F34.2, AC2).
            Assert.Equal(0, mock.RequestCount);
        }

        [Fact]
        public async Task TimeDateUsesTheTemplateAndSendsNoRequest()
        {
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(TimeDateRequest(), CancellationToken.None);

            // (F34.2, AC2).
            Assert.Equal(0, mock.RequestCount);
        }
    }

    public sealed class ScenarioPromptContract : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;
        LlmOptions options = null!;
        CapturedCompletionRequest captured = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            options = new LlmOptions
            {
                Endpoint = mock.BaseUri.ToString(),
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = 450,
            };
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new FakeHttpClientFactory(),
                new TestOptionsMonitor<LlmOptions>(options),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader());

            var track = new MediaItem(
                "m1", "/media/x.mp3", "Astral Plane", default, Artist: "Valerie June",
                Album: "The Order of Time", Genre: "Folk", Year: 2017);
            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave", track,
                new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4)), "test-station");

            await writer.WriteAsync(request, CancellationToken.None);
            captured = mock.Requests[0];
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void RequestBodyCarriesTheHouseScaffold()
        {
            var systemContent = ExtractMessageContent(captured.Body, "system");

            // System content includes the baked scaffold (radio DJ, 1–2 sentences, no stage
            // directions) (F34.3, AC3).
            Assert.Contains("radio DJ", systemContent);
        }

        [Fact]
        public void RequestBodyCarriesStationNameLocalTimeAndTrackTags()
        {
            var userContent = ExtractMessageContent(captured.Body, "user");
            var carriesEverything =
                userContent.Contains("GenWave") &&
                userContent.Contains("2026-06-09 14:37") &&
                userContent.Contains("Astral Plane") &&
                userContent.Contains("Valerie June") &&
                userContent.Contains("The Order of Time") &&
                userContent.Contains("Folk") &&
                userContent.Contains("2017");

            // title/artist/album/genre/year + station + LocalNow all present (F34.3, AC3).
            Assert.True(carriesEverything, userContent);
        }

        [Fact]
        public void RequestModelIsLlmModel()
        {
            using var doc = JsonDocument.Parse(captured.Body);

            // body.model == Llm:Model (F34.3, AC3).
            Assert.Equal(options.Model, doc.RootElement.GetProperty("model").GetString());
        }

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
    }

    public sealed class ScenarioCopyHygiene : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task WrappingQuotesAreStripped()
        {
            mock.ReplyContent = "\"Great tune incoming!\"";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // "\"Great tune incoming!\"" → Great tune incoming! (F34.5, AC4).
            Assert.Equal("Great tune incoming!", result.Text);
        }

        [Fact]
        public async Task MarkdownAndStageDirectionsAreStripped()
        {
            mock.ReplyContent = "*chuckles* **Next up**, we've got a treat.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // "*chuckles* **Next up**…" loses the asterisk artifacts (F34.5, AC4).
            Assert.Equal("Next up, we've got a treat.", result.Text);
        }

        [Fact]
        public async Task AChatPreambleBeforeTheQuotedBodyIsDropped()
        {
            // gh-#186 — observed live: the DJ read "Here's your lead-in copy:" on air. The
            // preamble ends in a colon, carries a meta word, and the quoted body runs to the end —
            // all three gates hold, so only the body survives (and its quotes unwrap as usual).
            mock.ReplyContent = "Here's your lead-in copy:\n\n\"Not my usual cup of tea, but here we go.\"";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("Not my usual cup of tea, but here we go.", result.Text);
        }

        [Fact]
        public async Task AnnouncerCopyWithAColonButNoMetaWordIsKeptIntact()
        {
            // gh-#186 guard — "Up next:" is ordinary announcer phrasing, no meta word: nothing
            // is dropped even though a colon precedes a quote. This is the ONLY guard left after
            // gh-#430 widened the strip to unquoted bodies too — see the meta-word-window pair
            // that StripChatPreamble's remarks call out as the whole heuristic.
            mock.ReplyContent = "Up next: \"Blue Monday\" by New Order, right here on GenWave.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("Up next: \"Blue Monday\" by New Order, right here on GenWave.", result.Text);
        }

        [Fact]
        public async Task LegitCopyWithAColonButNoMetaWordSurvivesUntouched()
        {
            // gh-#430 (c) — ordinary announcer copy that happens to contain a colon, and has no
            // meta word before it, must survive byte-identical: the two-gate heuristic (colon
            // position + meta word) is deliberately not widened any further than this.
            mock.ReplyContent = "Coming up at nine: a jazz number to ease you into the evening.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("Coming up at nine: a jazz number to ease you into the evening.", result.Text);
        }

        [Fact]
        public async Task AnUnquotedBodyAfterAPreambleIsStripped()
        {
            // gh-#430 root cause — the ORIGINAL bug: an unquoted body used to fail gate 3 (the
            // "quoted block running to end" requirement) and the whole string, preamble included,
            // aired live. Gates 1+2 (colon ≤80 chars, meta word present) are now sufficient on
            // their own; the body needs no quotes at all.
            mock.ReplyContent = "Here is the lead-in announcement: Tonight we're taking a detour "
                + "through some deep cuts.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("Tonight we're taking a detour through some deep cuts.", result.Text);
        }

        [Fact]
        public async Task AMetaWordWhoseQuoteDoesNotRunToTheEndStillDropsThePreamble()
        {
            // gh-#430 (b) — a body that OPENS with a quote but has trailing text after the
            // closing one is no longer a reason to keep the preamble around: the preamble is
            // still gone, and the embedded quote marks ride through untouched (StripWrappingQuotes
            // only unwraps a string that IS entirely one quoted block) — the sensible reading is
            // "the chat preamble is gone; the quoted title inside stays quoted".
            mock.ReplyContent = "Sure: \"Great tune\" - and plenty more where that came from.";
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal("\"Great tune\" - and plenty more where that came from.", result.Text);
        }
    }

    public sealed class ScenarioDisabledMeansTemplateOnly
    {
        [Fact]
        public async Task EmptyEndpointYieldsTemplateCopyWithNoHttpAttempt()
        {
            var (writer, holder, _) = BuildWriter(endpoint: "");

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Llm:Endpoint "" → template text; the writer never attempts a completion at all, so the
            // status holder — which only ever records a real attempt — stays untouched (F34.2, AC5).
            Assert.Null(holder.Last);
        }
    }

    public sealed class ScenarioStatusHolderRecordsAttempts : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ASuccessfulCompletionRecordsOkWithTimestamp()
        {
            var (writer, holder, _) = BuildWriter(mock.BaseUri.ToString());
            var before = DateTimeOffset.UtcNow;

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // lastOutcome ok + lastAttemptAt set (F34.8, AC6).
            Assert.True(holder.Last is { Outcome: LlmAttemptOutcome.Ok } status && status.AttemptedAt >= before);
        }

        [Fact]
        public async Task AFailedCompletionRecordsFailedWithTimestamp()
        {
            mock.Mode = MockCompletionsMode.Fail;
            var (writer, holder, _) = BuildWriter(mock.BaseUri.ToString());
            var before = DateTimeOffset.UtcNow;

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // (F34.8, AC6).
            Assert.True(holder.Last is { Outcome: LlmAttemptOutcome.Failed } status && status.AttemptedAt >= before);
        }
    }

    // ---------------------------------------------------------------------
    // gh-#429 — the call ring names which persona authored each call, success or failure
    // ---------------------------------------------------------------------

    public sealed class ScenarioCallRingRecordsThePersonaName : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        static (LlmCopyWriter Writer, LlmCallRing Ring) BuildWriterWithRing(string endpoint, Persona? persona)
        {
            var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
            var writer = new LlmCopyWriter(
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
                new FakeActivePersonaAccessor { Persona = persona },
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader());
            return (writer, ring);
        }

        [Fact]
        public async Task ASuccessfulCallRecordsTheActivePersonasName()
        {
            var persona = new Persona(1, "Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_sky",
                DateTime.UtcNow, DateTime.UtcNow);
            var (writer, ring) = BuildWriterWithRing(mock.BaseUri.ToString(), persona);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var record = Assert.Single(ring.Snapshot());
            Assert.Equal("Neon Nightowl", record.PersonaName);
        }

        [Fact]
        public async Task AFailedCallStillRecordsTheActivePersonasName()
        {
            mock.Mode = MockCompletionsMode.Fail;
            var persona = new Persona(2, "The Archivist", "Keeper of the catalog.", "dry, precise", "af_sky",
                DateTime.UtcNow, DateTime.UtcNow);
            var (writer, ring) = BuildWriterWithRing(mock.BaseUri.ToString(), persona);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var record = Assert.Single(ring.Snapshot());
            Assert.Equal("The Archivist", record.PersonaName);
        }

        [Fact]
        public async Task NoActivePersonaRecordsANullPersonaName()
        {
            var (writer, ring) = BuildWriterWithRing(mock.BaseUri.ToString(), persona: null);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var record = Assert.Single(ring.Snapshot());
            Assert.Null(record.PersonaName);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — every miss lands on the template rung
    // ---------------------------------------------------------------------

    public sealed class ScenarioFallbackLadder : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task TimeoutFallsBackToTemplateWithOneWarn()
        {
            mock.Mode = MockCompletionsMode.Delay;
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var (writer, _, logger) = BuildWriter(mock.BaseUri.ToString(), timeoutSeconds: 1);

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // Mock stalls past Llm:TimeoutSeconds → template copy, WARN, render proceeds (F34.4, AC7).
            Assert.True(result.Text == expected && logger.Warnings.Count == 1);
        }

        [Fact]
        public async Task Non2xxFallsBackToTemplate()
        {
            mock.Mode = MockCompletionsMode.Fail;
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // 500 → template (F34.4, AC8).
            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public async Task ConnectFailureFallsBackToTemplate()
        {
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            // Port 1 is a reserved low port nothing in this sandbox listens on — connecting to it is
            // a deterministic refused-connection failure without racing for a free ephemeral port.
            var (writer, _, _) = BuildWriter("http://127.0.0.1:1");

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // No server at the endpoint → template (F34.4, AC8).
            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public async Task EmptyCopyFallsBackToTemplate()
        {
            mock.ReplyContent = "   \n  ";
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // Whitespace-only completion → rejected (F34.4, AC9).
            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public async Task OverLongCopyIsRejectedNeverTruncated()
        {
            mock.ReplyContent = "This completion is intentionally far longer than the configured limit.";
            var request = LeadInRequest();
            var expected = await TemplateTextAsync(request);
            var (writer, _, _) = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 10);

            var result = await writer.WriteAsync(request, CancellationToken.None);

            // > Llm:MaxCopyChars after cleanup → template copy, and the LLM text is not
            // truncated mid-sentence into the render (F34.5, AC9).
            Assert.Equal(expected, result.Text);
        }
    }
}
