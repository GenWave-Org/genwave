// STORY-537 — The air path is not delayed (gh-#293 gh-#920 · SPEC F218 · PLAN T704)
//
// BDD specification — xUnit. AC2 only; the other ACs live in tests/GenWave.Memory.Tests/Specs/Story537_AnInventedLifeThatStaysConsistent.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheAirPathIsNotDelayed
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — The air path is not delayed
    public sealed class ScenarioTheAirPathIsNotDelayed
    {
        // entry point: LlmCopyWriter.WriteAsync with an ILifeExtractor fake
        // The guarantee is structural: ILifeExtractor.Enqueue is a synchronous void the writer never waits on, so
        // the extractor's own slowness cannot reach the render. That slowness is pinned Memory-side, by
        // FeatureAnInventedLifeThatStaysConsistent.ScenarioTheModelIsSlow.
        // When  WriteAsync accepts a copy

        [Fact]
        public async Task TheAcceptedCopyIsReturnedAndTheExtractorWasHandedItExactlyOnce()
        {
            var extractor = new FakeLifeExtractor();

            var copy = await WriteAsync(extractor, "Up next, Snow Patrol, a good one.");

            Assert.Equal("Up next, Snow Patrol, a good one.", copy.Text);
            Assert.Single(extractor.Received);
        }

        [Fact]
        public async Task TheAcceptedCopyWasHandedToTheExtractorWithItsPersona()
        {
            var extractor = new FakeLifeExtractor();

            await WriteAsync(extractor, "Up next, Snow Patrol, a good one.");

            Assert.Equal([new AcceptedCopy(7, "Sal", "Up next, Snow Patrol, a good one.")], extractor.Received);
        }

        [Fact]
        public async Task ATemplateFallbackIsNeverHandedToTheExtractor()
        {
            var extractor = new FakeLifeExtractor();

            var copy = await WriteAsync(extractor, reply: null);

            Assert.False(copy.FreshPerAiring);
            Assert.Empty(extractor.Received);
        }

        static async Task<SegmentCopy> WriteAsync(ILifeExtractor extractor, string? reply)
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                reply is null
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } }),
                            Encoding.UTF8, "application/json"),
                    }));
            var now = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new SingleHandlerHttpClientFactory(handler),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions
                {
                    Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 450,
                }),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor { Persona = new Persona(7, "Sal", "", "", "", DateTime.UnixEpoch, DateTime.UnixEpoch) },
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader(),
                new FakeStationClockProvider(now),
                lifeExtractor: extractor);

            var request = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave",
                new MediaItem("m1", "/media/x.mp3", "Snow Patrol", default, "Someone"), now, "test-station");
            return await writer.WriteAsync(request, CancellationToken.None);
        }
    }
}
