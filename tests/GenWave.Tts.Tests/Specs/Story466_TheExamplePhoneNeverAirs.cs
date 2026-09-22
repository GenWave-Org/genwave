// STORY-466 — The example phone never airs (gh-#701 · SPEC F199 · PLAN T549 T550)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheexamplephoneneverairs
{
    const string Pending = "pending: T549 — The example phone never airs (STORY-466)";

    public sealed class ScenarioThePromptForOneSponsor
    {
        // Given: AdScriptPromptBuilder for slug "acme", built twice

        /// <summary>AC1 — the same 555-01xx both times</summary>
        [Fact(Skip = Pending)]
        public void IsDeterministic() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThePromptsForTwoSponsors
    {
        // Given: slugs "acme" and "zenith"

        /// <summary>AC2 — different examples</summary>
        [Fact(Skip = Pending)]
        public void DiffersPerSponsor() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAScriptWithTheExample
    {
        // Given: "555-0142" in the script, sponsor phone "812-555-0199", AdScriptWriter hygiene (T550)

        /// <summary>AC3 — the sponsor phone is present</summary>
        [Fact(Skip = Pending)]
        public void SaysTheSponsorPhone() => Assert.Fail(Pending);

        /// <summary>AC3 — no 555-0142 remains</summary>
        [Fact(Skip = Pending)]
        public void DropsTheExample() => Assert.Fail(Pending);
    }

    public sealed class ScenarioASponsorWhoseOwnPhoneIs555
    {
        // Given: sponsor phone "555-0100", script says it

        /// <summary>AC4 — unchanged</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheSponsorsOwnFiveFiveFive() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASponsorWithNoPhone
    {
        // Given: no phone; line "Call 555-0142 today for a quote."

        /// <summary>AC5 — the clause is dropped</summary>
        [Fact(Skip = Pending)]
        public void DropsTheClause() => Assert.Fail(Pending);

        /// <summary>AC5 — no digits remain</summary>
        [Fact(Skip = Pending)]
        public void LeavesNoDigits() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAStrayNumberAfterHygiene
    {
        // Given: post-hygiene script carrying "555-0199" ≠ sponsor phone, F160 validation

        /// <summary>AC6 — the phone rule fails</summary>
        [Fact(Skip = Pending)]
        public void StillFailsTheValidator() => Assert.Fail(Pending);
    }

}
