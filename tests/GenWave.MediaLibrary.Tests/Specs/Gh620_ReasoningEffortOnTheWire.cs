// gh-#620 — the reasoning control on the wire: the two MediaLibrary posters
//
// BDD specification — xUnit. OllamaMoodTagger and OllamaExplicitClassifier each post their own
// /v1/chat/completions request from their own options class (both bind the same "Llm" section),
// so each is pinned separately here against a captured request body (the Story216/Story251
// FakeHttpMessageHandler idiom — no network, no Postgres). Companion files: Core.Tests owns the
// vocabulary, Tts.Tests the copy/crosstalk writers, Host.Tests the setting + the wish parser.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.MediaLibrary.ExplicitClassification;
using GenWave.MediaLibrary.Mood;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureReasoningEffortOnTheWire
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Serves one fixed reply and appends every request body to <paramref name="bodies"/>
    /// (read inside the responder — the posters dispose their HttpRequestMessage afterwards).</summary>
    static FakeHttpMessageHandler Capturing(List<string> bodies, string content) =>
        new(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }),
            };
        });

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

    static OllamaMoodTagger Tagger(FakeHttpMessageHandler handler, string reasoningEffort) =>
        new(new HttpClient(handler), new FakeOptionsMonitor<MoodTaggerOptions>(
            new MoodTaggerOptions { Endpoint = "http://fake-llm", Model = "test-model", ReasoningEffort = reasoningEffort }));

    static OllamaExplicitClassifier Classifier(FakeHttpMessageHandler handler, string reasoningEffort) =>
        new(new HttpClient(handler), new FakeOptionsMonitor<ExplicitClassifierOptions>(
            new ExplicitClassifierOptions { Endpoint = "http://fake-llm", Model = "test-model", ReasoningEffort = reasoningEffort }));

    // ---------------------------------------------------------------------
    // HAPPY PATH — mood tagger
    // ---------------------------------------------------------------------

    public sealed class ScenarioMoodTagger
    {
        [Fact]
        public async Task The_default_request_carries_reasoning_effort_none()
        {
            var bodies = new List<string>();
            await Tagger(Capturing(bodies, "warm"), new MoodTaggerOptions().ReasoningEffort)
                .TagAsync("Artist", "Sunny Skies", "Pop", CancellationToken.None);

            Assert.Equal("none", ReasoningEffortOf(Assert.Single(bodies)));
        }

        [Fact]
        public async Task Omit_leaves_the_field_out_of_the_request_entirely()
        {
            var bodies = new List<string>();
            await Tagger(Capturing(bodies, "warm"), "omit")
                .TagAsync("Artist", "Sunny Skies", "Pop", CancellationToken.None);

            Assert.False(CarriesReasoningEffort(Assert.Single(bodies)));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — explicit classifier
    // ---------------------------------------------------------------------

    public sealed class ScenarioExplicitClassifier
    {
        [Fact]
        public async Task The_default_request_carries_reasoning_effort_none()
        {
            var bodies = new List<string>();
            await Classifier(Capturing(bodies, "no"), new ExplicitClassifierOptions().ReasoningEffort)
                .ClassifyAsync("Artist", "Sunny Skies", CancellationToken.None);

            Assert.Equal("none", ReasoningEffortOf(Assert.Single(bodies)));
        }

        [Fact]
        public async Task Omit_leaves_the_field_out_of_the_request_entirely()
        {
            var bodies = new List<string>();
            await Classifier(Capturing(bodies, "no"), "omit")
                .ClassifyAsync("Artist", "Sunny Skies", CancellationToken.None);

            Assert.False(CarriesReasoningEffort(Assert.Single(bodies)));
        }
    }
}
