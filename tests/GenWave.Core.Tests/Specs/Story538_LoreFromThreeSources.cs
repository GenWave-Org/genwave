// STORY-538 — Lore from three sources (gh-#293 gh-#920 · SPEC F218 · PLAN T705)
//
// BDD specification — xUnit. AC1 (the card half) here; AC2–AC4 in tests/GenWave.Host.Tests/Specs/Story538_LoreFromThreeSources.cs; AC5 in tests/GenWave.Memory.Tests/Specs/Story538_PendingLoreIsSilent.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Core.Tests.Specs;

public static class FeatureLoreFromThreeSources
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — The card's Lore imports
    public sealed class ScenarioTheCardSLoreImports
    {
        // entry point: PersonaCard JSON import (the card half; the row lands via the Host import path)
        // Given a card with Lore ["Lost the 1998 regional DJ final to a coin toss"]
        // When  it is imported

        [Fact(Skip = "pending: T705 — see docs/PLAN.md")]
        public void ThePersonaHasAPersonaMemoryRowKindLoreSourceAuthoredWithThatText() => Assert.Fail("pending: T705 — the persona has a persona_memory row kind lore, source authored, with that text");
    }
}
