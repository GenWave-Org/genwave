// STORY-123 — LlmCopyWriter.WritePreviewAsync: the real preview seam, no template fallback
//
// BDD specification — xUnit. Proves IPersonaPreviewWriter's one production implementation
// (LlmCopyWriter) reuses WriteAsync's own prompt-building + hygiene code (same mock server, same
// prompt-contract assertions as Story119/Story121) while never degrading an LLM miss to template
// text — that would misrepresent the persona being auditioned (SPEC F35.6). PersonaController
// (Host.Tests, Story123_PreviewEndpoints.cs) fakes this seam at the controller boundary; this file
// is where the real HTTP/hygiene/prompt behavior is exercised.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePersonaPreviewWriter
{
    static SegmentRequest LeadInRequest(MediaItem? track = null) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            track ?? new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static (LlmCopyWriter Writer, LlmCopyStatusHolder Holder) BuildWriter(
        string endpoint, int timeoutSeconds = 5, int maxCopyChars = 450, int previewQueueWaitSeconds = 5)
    {
        var holder = new LlmCopyStatusHolder();
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = timeoutSeconds,
                MaxCopyChars = maxCopyChars,
                PreviewQueueWaitSeconds = previewQueueWaitSeconds,
            }),
            holder,
            new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>(),
            TimeProvider.System,
            new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
            new FakeDegradationModeReader());
        return (writer, holder);
    }

    static async Task<string> TemplateTextAsync(SegmentRequest request) =>
        (await new TemplateCopyWriter(new PatterTemplateRenderer()).WriteAsync(request, CancellationToken.None)).Text;

    // ---------------------------------------------------------------------
    // HAPPY PATH — LeadIn/BackAnnounce hit the LLM, exactly like WriteAsync
    // ---------------------------------------------------------------------

    public sealed class ScenarioLlmReachable : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task LeadInPreviewReturnsSuccessWithTheMockCompletion()
        {
            mock.ReplyContent = "Spinning up something great, stick around.";
            var (writer, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            var success = Assert.IsType<PersonaPreviewResult.Success>(result);
            Assert.Equal(mock.ReplyContent, success.Text);
        }

        [Fact]
        public async Task PersonaOverrideReachesTheSamePromptTheOnAirWriterWouldBuild()
        {
            // The SAME BuildPersonaSection/BuildSystemPrompt code WriteAsync uses — proves the
            // preview never builds a parallel prompt (F35.6).
            var persona = new Persona(9, "Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_sky",
                DateTime.UtcNow, DateTime.UtcNow);
            var (writer, _) = BuildWriter(mock.BaseUri.ToString());

            await writer.WritePreviewAsync(LeadInRequest(), persona, CancellationToken.None);

            var systemContent = ExtractMessageContent(mock.Requests[0].Body, "system");
            Assert.Contains("Spins vinyl til dawn.", systemContent);
            Assert.Contains("moody, late-night", systemContent);
        }

        [Fact]
        public async Task PreviewNeverRecordsToTheOnAirStatusHolder()
        {
            // The status holder feeds GET /api/status's on-air lastOutcome/lastAttemptAt (STORY-125)
            // — preview activity never airs and must not appear there.
            var (writer, holder) = BuildWriter(mock.BaseUri.ToString());

            await writer.WritePreviewAsync(LeadInRequest(), null, CancellationToken.None);

            Assert.Null(holder.Last);
        }
    }

    public sealed class ScenarioTemplatedKindsRouteToTemplate : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task StationIdPreviewReturnsTemplateTextAndSendsNoRequest()
        {
            // Not a fallback — StationId never touches the LLM on-air either (F34.2).
            var expected = await TemplateTextAsync(StationIdRequest());
            var (writer, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WritePreviewAsync(StationIdRequest(), null, CancellationToken.None);

            var success = Assert.IsType<PersonaPreviewResult.Success>(result);
            Assert.Equal(expected, success.Text);
            Assert.Equal(0, mock.RequestCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an LLM miss is reported, never silently templated
    // ---------------------------------------------------------------------

    public sealed class ScenarioLlmMissIsReportedNotFallenBackTo : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task DisabledEndpointYieldsFailed()
        {
            var (writer, _) = BuildWriter(endpoint: "");

            var result = await writer.WritePreviewAsync(LeadInRequest(), null, CancellationToken.None);

            Assert.IsType<PersonaPreviewResult.Failed>(result);
        }

        [Fact]
        public async Task TimeoutYieldsFailedNeverTemplateText()
        {
            mock.Mode = MockCompletionsMode.Delay;
            var request = LeadInRequest();
            var templateText = await TemplateTextAsync(request);
            var (writer, _) = BuildWriter(mock.BaseUri.ToString(), timeoutSeconds: 1);

            var result = await writer.WritePreviewAsync(request, null, CancellationToken.None);

            var failed = Assert.IsType<PersonaPreviewResult.Failed>(result);
            Assert.NotEqual(templateText, failed.Detail);
        }

        [Fact]
        public async Task Non2xxYieldsFailed()
        {
            mock.Mode = MockCompletionsMode.Fail;
            var (writer, _) = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WritePreviewAsync(LeadInRequest(), null, CancellationToken.None);

            Assert.IsType<PersonaPreviewResult.Failed>(result);
        }

        [Fact]
        public async Task OverLongCopyYieldsFailedNeverTruncated()
        {
            mock.ReplyContent = "This completion is intentionally far longer than the configured limit.";
            var (writer, _) = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 10);

            var result = await writer.WritePreviewAsync(LeadInRequest(), null, CancellationToken.None);

            Assert.IsType<PersonaPreviewResult.Failed>(result);
        }
    }

    // ---------------------------------------------------------------------
    // GATE BUSY — a preview declines fast instead of queueing behind on-air
    // ---------------------------------------------------------------------

    public sealed class ScenarioGateHeldByOnAirRender : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync(MockCompletionsMode.Delay);

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task PreviewReturnsBusyInsteadOfQueueingBehindTheSingleFlightGate()
        {
            // Generous on-air timeout so the Delay-mode call provably holds the gate for the whole
            // test; zero preview wait so the decline is immediate once the gate is seen held.
            var (writer, _) = BuildWriter(mock.BaseUri.ToString(), timeoutSeconds: 60, previewQueueWaitSeconds: 0);

            using var onAirCts = new CancellationTokenSource();
            var onAir = writer.WriteAsync(LeadInRequest(), onAirCts.Token);

            // The gate is only held once the on-air call's HTTP request is in flight — wait for the
            // mock to see it before asserting anything about contention.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (mock.RequestCount == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(25);
            Assert.True(mock.RequestCount > 0, "the on-air render never reached the mock LLM");

            var result = await writer.WritePreviewAsync(LeadInRequest(), null, CancellationToken.None);

            Assert.IsType<PersonaPreviewResult.Busy>(result);

            onAirCts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => onAir);
        }
    }

    static string ExtractMessageContent(string body, string role)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == role)
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }
}
