// STORY-539 - T725: the lore lines carry a nudge; episodes and life do not.

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

        [Fact]
        public void TheNudgeFollowsTheLoreLines() =>
            Assert.Equal(["- the van", LlmPromptBuilder.MemoryLoreNudgeLine, "- the quiz"], blocks.Skip(1).Take(3));

        [Fact]
        public void TheBlockStillSaysTheDjNeverHasTo() => Assert.Contains(LlmPromptBuilder.MemoryMayReferLine, blocks);
    }

    public sealed class ScenarioNoLore
    {
        [Fact]
        public void EpisodesOnlyCarryNoNudge() =>
            Assert.DoesNotContain(LlmPromptBuilder.MemoryLoreNudgeLine, Blocks([], [Row(MemoryKinds.Episode, "the quiz")]));

        [Fact]
        public void NothingRememberedCarriesNoNudge() =>
            Assert.DoesNotContain(LlmPromptBuilder.MemoryLoreNudgeLine, Blocks([], []));
    }
}
