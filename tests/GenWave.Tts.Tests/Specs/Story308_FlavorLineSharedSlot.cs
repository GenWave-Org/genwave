// STORY-308 — The flavor line shares the slot (F116.3, amends F107.5)
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the shared-slot arbitration lands at T249 (serialized behind T248 — both
// touch prompt assembly; the T224→T225 precedent). The one law this file exists to hold:
// a break's prompt carries AT MOST ONE extra line, and the ceiling never grows past F107.

namespace GenWave.Tts.Tests.Specs;

using Xunit;

public static class FeatureFlavorLineSharedSlot
{
    public sealed class ScenarioTheShowLineAirs
    {
        [Fact(Skip = "Pending (T249)")]
        public void ShowLineAppearsWhenDueAndNoContextFact()
        {
            // Given Station:Shows:PatterCadenceMinutes elapsed, a show on the air, no due fact
            // When  a lead-in prompt is built
            // Then  exactly one show-flavor line is present
        }
    }

    public sealed class ScenarioContextWinsTheSlot
    {
        [Fact(Skip = "Pending (T249)")]
        public void ContextLineAppearsAndShowLineDoesNot()
        {
            // Given a due context fact AND a due show line
            // When  the prompt is built
            // Then  the context line appears and the show line does not (facts beat identity)
        }

        [Fact(Skip = "Pending (T249)")]
        public void ShowGateStaysOpenAfterLosingTheSlot()
        {
            // Given the show line lost the slot to a context fact
            // When  the next eligible break's prompt is built (no fact due)
            // Then  the show line appears — losing the slot never consumed the cadence
        }
    }

    public sealed class ScenarioClosedGateIsByteIdentical
    {
        [Fact(Skip = "Pending (T249)")]
        public void ClosedGateMatchesTheF107Golden()
        {
            // Given cadence not elapsed, or the setting at its 0 default, or no show on air
            // When  the prompt is built
            // Then  output matches the F107 golden byte-for-byte (the Story298 pin extended)
        }
    }
}
