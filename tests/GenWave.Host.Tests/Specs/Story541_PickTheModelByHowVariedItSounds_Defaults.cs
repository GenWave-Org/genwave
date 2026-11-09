// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T697)
//
// BDD specification — xUnit.
//
// AC4 amended 2026-11-09 per Dean, "recommend, don't default": the bench's pick is recommended in
// the Settings model picker, and F34.2 keeps Llm:Model blank so LLM copy stays off on a fresh
// install (Story151 owns that blank).
//
// Spans three files; see tests/GenWave.Memory.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Bench.cs for the map.

using System.Net.Http.Json;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsDefaults
{
    static JsonElement LlmModelSetting(LiveChoiceListsWebFactory factory)
    {
        var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
        var body = JsonDocument.Parse(client.GetStringAsync("/api/settings").GetAwaiter().GetResult()).RootElement;
        return body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Llm:Model");
    }

    static LiveChoiceListsWebFactory FactoryListing(params string[] modelIds) => new(
        llmEndpoint: "http://fake-llm.local",
        respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data = modelIds }),
        }),
        ttsVoiceIds: []);

    static List<(string? Value, string? Label)> ChoicePairs(JsonElement setting) =>
        setting.GetProperty("choices").EnumerateArray()
            .Select(c => (c.GetProperty("value").GetString(), c.GetProperty("label").GetString()))
            .ToList();

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — the bench's pick is recommended in the model picker
    // entry point: WebApplicationFactory<Program> GET /api/settings (Llm:Model choices)
    public sealed class ScenarioTheBenchPickIsRecommendedInTheModelPicker : IDisposable
    {
        // Given the endpoint lists [mistral:7b, llama3.1:8b, mistral-nemo:12b]
        // When  the Settings page loads

        readonly LiveChoiceListsWebFactory factory;
        readonly List<(string? Value, string? Label)> choices;
        readonly string? savedValue;

        public ScenarioTheBenchPickIsRecommendedInTheModelPicker()
        {
            factory = FactoryListing("mistral:7b", "llama3.1:8b", "mistral-nemo:12b");
            var setting = LlmModelSetting(factory);
            choices = ChoicePairs(setting);
            savedValue = setting.GetProperty("value").GetString();
        }

        public void Dispose() => factory.Dispose();

        [Fact]
        public void TheFirstChoiceIsTheBenchPick() => Assert.Equal("mistral-nemo:12b", choices[0].Value);

        [Fact]
        public void TheFirstChoiceIsLabelledRecommended() =>
            Assert.Equal("mistral-nemo:12b (recommended)", choices[0].Label);

        [Fact]
        public void TheBenchPickLeadsAndTheRestKeepTheirOrder() => Assert.Equal(
            ["mistral-nemo:12b", "mistral:7b", "llama3.1:8b"], choices.Select(c => c.Value));

        [Fact]
        public void LlmModelOnAFreshDeployIsStillBlank() => Assert.Equal("", savedValue);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC4 — the endpoint does not list the pick
    public sealed class ScenarioTheEndpointLacksTheBenchPick : IDisposable
    {
        // Given the endpoint lists [mistral:7b, llama3.1:8b]
        // When  the Settings page loads

        readonly LiveChoiceListsWebFactory factory;
        readonly List<(string? Value, string? Label)> choices;

        public ScenarioTheEndpointLacksTheBenchPick()
        {
            factory = FactoryListing("mistral:7b", "llama3.1:8b");
            choices = ChoicePairs(LlmModelSetting(factory));
        }

        public void Dispose() => factory.Dispose();

        [Fact]
        public void ChoicesAreTheListersListUnchanged() => Assert.Equal(
            [("mistral:7b", "mistral:7b"), ("llama3.1:8b", "llama3.1:8b")], choices);
    }
}
