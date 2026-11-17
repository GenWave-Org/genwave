// STORY-539 — The recall budget (gh-#293 gh-#920 · SPEC F218 · PLAN T700, amended T737)
//
// T737 retired the row-recall facts (2 lore / 3 episodes / 3 life per prompt, the 48 h and shift gaps, mark-aired
// before render): since SPEC F218.40 a callback break carries exactly one memory and no other break carries
// memory rows, so those rules moved to the callback planner (Story548). What stays is the on/off switch.

using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheRecallBudget
{
    // AC — Memory off is silent
    public sealed class ScenarioMemoryOff
    {
        readonly MemoryPacket packet;

        public ScenarioMemoryOff()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Life, "has a beagle named Gus", source: "extracted", forgottenAgo: TimeSpan.FromHours(1));
            packet = new MemoryRecallSource(store).NeverMentionAsync(1, MemoryBudget.Off, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void ThePacketIsEmpty() => Assert.Empty(packet.NeverMention);
    }

    // AC — Memory on carries only the never-mention list
    public sealed class ScenarioMemoryOn
    {
        readonly MemoryPacket packet;

        public ScenarioMemoryOn()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Life, "has a beagle named Gus", source: "extracted", forgottenAgo: TimeSpan.FromHours(1));
            store.Add(MemoryKinds.Lore, "lore 1");
            packet = new MemoryRecallSource(store).NeverMentionAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void OnlyTheForgottenClaimIsListed() => Assert.Equal(["has a beagle named Gus"], packet.NeverMention);
    }
}
