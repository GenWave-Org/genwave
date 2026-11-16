// STORY-538 — Pending lore is silent (gh-#293 gh-#920 · SPEC F218 · PLAN T700)
//
// BDD specification — xUnit. AC5 only; the other ACs live in the Core and Host Story538 files.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Specs;

public static class FeaturePendingLoreIsSilent
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — Pending is silent
    public sealed class ScenarioPendingIsSilent
    {
        // entry point: IMemoryRecallSource over the in-memory store
        // Given a drafted row with approved_at null
        // When  the prompt is built (asserted on the MemoryPacket; the prompt block lands in T701)

        readonly MemoryPacket packet;

        public ScenarioPendingIsSilent()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Lore, "a pending draft", source: "drafted");
            store.Add(MemoryKinds.Lore, "an approved draft", source: "drafted", approved: true);
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheRowIsNotRecalled() => Assert.DoesNotContain("a pending draft", packet.Lore.Select(m => m.Content));

        [Fact]
        public void AnApprovedDraftIsRecalled() => Assert.Equal(["an approved draft"], packet.Lore.Select(m => m.Content));
    }
}
