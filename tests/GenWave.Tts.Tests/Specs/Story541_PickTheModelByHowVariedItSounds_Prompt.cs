// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T695)
//
// Spans three files; see tests/GenWave.Memory.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Bench.cs for the map.

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.PatterBench;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsPrompt
{
    const string PlainCardJson = """
        {"schemaVersion":1,"name":"Plain Pat","tagline":"Keeps it simple.","soul":"Pat keeps every break short and warm.",
         "quirks":[],"voice":{"engine":"kokoro","voiceId":"am_adam","pace":1.0,"language":"en"},
         "energyDisposition":0.5,"lore":[],"corrections":[]}
        """;

    // AC2 — the real prompt
    // entry point: Cli.RunAsync (the prompt it posts) vs LlmPromptBuilder
    public sealed class ScenarioTheRealPrompt : IAsyncLifetime
    {
        // Given a persona card and the fixture track list
        // When  a break prompt is built
        readonly string directory = Path.Combine(Path.GetTempPath(), $"patterbench-{Guid.NewGuid():N}");
        readonly CannedChatHandler handler = new(_ => "Here comes a real classic, turn it up loud.");
        JsonElement messages;

        public async Task InitializeAsync()
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "plain.persona.json"), PlainCardJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            await Cli.RunAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "m", "--personas", "plain", "-n", "1",
                 "--out", Path.Combine(directory, "report.md")],
                stdout, stderr, handler, new FakeTimeProvider(), directory);
            messages = JsonDocument.Parse(Assert.Single(handler.ChatBodies)).RootElement.GetProperty("messages").Clone();
        }

        public Task DisposeAsync()
        {
            Directory.Delete(directory, recursive: true);
            return Task.CompletedTask;
        }

        static (string System, string User) Expected()
        {
            var card = PersonaCardSerializer.Deserialize(PlainCardJson);
            var mentionOwnName = new Random(BenchFixtures.Seed).NextDouble() < LlmPromptBuilder.SelfNameMentionProbability;
            var request = new SegmentRequest(
                SegmentKind.LeadIn, "bench", BenchFixtures.StationName, BenchFixtures.Tracks[0],
                BenchFixtures.LocalNow, "bench", PersonaName: card?.Name);
            var clockLine = LlmPromptBuilder.BuildStationClockLine(BenchFixtures.LocalNow);
            return (
                LlmPromptBuilder.BuildSystemPrompt(
                    LlmPromptBuilder.BuildPersonaSection(null, card, mentionOwnName), new LlmOptions().MaxCopyChars, BenchFixtures.LocalNow),
                LlmPromptBuilder.BuildUserContent(request, clockLine, []));
        }

        [Fact]
        public void ItIsByteEqualToLlmPromptBuilderSOutputForTheSameInputs() =>
            Assert.Equal(
                Expected(),
                (messages[0].GetProperty("content").GetString() ?? "", messages[1].GetProperty("content").GetString() ?? ""));
    }
}
