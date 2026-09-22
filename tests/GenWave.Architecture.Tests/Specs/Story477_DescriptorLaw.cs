// STORY-477 — Descriptor law (gh-#778 · SPEC F205.6 · PLAN T575)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureDescriptorlaw
{
    const string Pending = "pending: T575 — Descriptor law (STORY-477)";

    public sealed class ScenarioKeysAndCopy
    {
        // Given: AllowedSetting keys × Settings.resx entries

        /// <summary>AC9 — every key has .Label and .Help</summary>
        [Fact(Skip = Pending)]
        public void EveryKeyHasCopy() => Assert.Fail(Pending);

        /// <summary>AC10 — every resx {Key}.* names a key</summary>
        [Fact(Skip = Pending)]
        public void NoOrphanCopy() => Assert.Fail(Pending);

        /// <summary>AC11 — every Number key has Min and Max</summary>
        [Fact(Skip = Pending)]
        public void EveryNumberIsRanged() => Assert.Fail(Pending);

        /// <summary>AC12 — every static Choice value has a label entry</summary>
        [Fact(Skip = Pending)]
        public void EveryChoiceIsLabelled() => Assert.Fail(Pending);
    }

}
