// STORY-567 — The shadow week (SPEC F220 · PLAN F220)
//
// AC6 · F220.21 · T787. The prompts today's builders send, captured before any shaper moves, as files under
// tests/GenWave.Tts.Tests/Golden/ (one per prompt: <name>.system.txt / <name>.user.txt). Entry points: the real
// LlmCopyWriter, CrosstalkScriptWriter and AdScriptWriter, with fakes only at the HTTP, store, clock and random edges.
//
// Regenerating (only after a deliberate prompt change, and review the diff):
//   GENWAVE_UPDATE_GOLDEN=1 dotnet test tests/GenWave.Tts.Tests --filter "FullyQualifiedName~FeatureGoldenPrompts"
// The default is compare. The switch writes the source files and refuses to run when CI is set.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Tts.Tests.Fakes;

public static class FeatureGoldenPrompts
{
    // AC6 — the prompt fixture set captured before any shaper moved
    public sealed class ScenarioTodaysPromptsAgainstTheCapturedOnes : IAsyncLifetime
    {
        IReadOnlyList<GoldenPrompt> djLines = [];
        GoldenPrompt chat = new("", "", "");
        GoldenPrompt ad = new("", "", "");

        public async Task InitializeAsync()
        {
            djLines = await GoldenPromptFixtures.DjLinesAsync();
            chat = await GoldenPromptFixtures.ChatAsync();
            ad = await GoldenPromptFixtures.AdAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        static string Mismatches(IEnumerable<GoldenPrompt> prompts, Func<GoldenPrompt, string> text, string part) =>
            string.Join(", ", GoldenFiles.Mismatches(prompts.Select(p => ($"{p.Name}.{part}.txt", text(p)))));

        static string BothMismatches(GoldenPrompt prompt) =>
            string.Join(", ", new[] { Mismatches([prompt], p => p.System, "system"), Mismatches([prompt], p => p.User, "user") }
                .Where(m => m.Length > 0));

        [Fact]
        public void EachDjLineSystemPromptMatches() => Assert.Equal("", Mismatches(djLines, p => p.System, "system"));

        [Fact]
        public void EachDjLineUserPromptMatches() => Assert.Equal("", Mismatches(djLines, p => p.User, "user"));

        [Fact]
        public void TheChatPromptMatches() => Assert.Equal("", BothMismatches(chat));

        [Fact]
        public void TheAdPromptMatches() => Assert.Equal("", BothMismatches(ad));
    }
}
