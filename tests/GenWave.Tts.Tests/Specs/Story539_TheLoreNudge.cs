// STORY-539 - T725, amended by T735: the lore nudge, the may-refer line and the memory rows are RETIRED by
// SPEC F218.40-F218.42 (only a callback ask carries one memory). These facts now pin their absence; the nudge and may-refer facts were merged into one absence fact.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheLoreNudge
{
    static RecalledMemory Row(string kind, string text) => new(1, kind, text, null);

    static IReadOnlyList<string> Blocks(IReadOnlyList<RecalledMemory> lore, IReadOnlyList<RecalledMemory> episodes) =>
        LlmPromptBuilder.BuildMemoryBlocks(new MemoryPacket(lore, episodes, [], []));

    public sealed class ScenarioLorePresent
    {
        readonly IReadOnlyList<string> blocks = Blocks([Row(MemoryKinds.Lore, "the van")], [Row(MemoryKinds.Episode, "the quiz")]);

        // F218.40-F218.42: the rows, the nudge and the may-refer line no longer reach the prompt.
        [Fact]
        public void NoRowsNudgeOrMayReferLineReachThePrompt() => Assert.Empty(blocks);
    }

    public sealed class ScenarioNoLore
    {
        [Fact]
        public void EpisodesOnlyCarryNothing() => Assert.Empty(Blocks([], [Row(MemoryKinds.Episode, "the quiz")]));

        [Fact]
        public void NothingRememberedCarriesNothing() => Assert.Empty(Blocks([], []));
    }
}
