// STORY-466 — The example phone never airs (gh-#701 · SPEC F199 · PLAN T549 T550)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Tts.Tests.Specs;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

public static class FeatureTheexamplephoneneverairs
{
    const string PendingT550 = "pending: T550 — The example phone never airs (STORY-466)";

    static readonly Regex ExamplePhonePattern = new(@"555-01\d{2}", RegexOptions.Compiled);

    static AdScriptWriteRequest Request(string sponsorName) =>
        new(sponsorName, null, null, 30, AudiencePosture.Everyone, 200, 0.4);

    public sealed class ScenarioThePromptForOneSponsor
    {
        // Given: AdScriptPromptBuilder for slug "acme", built twice
        readonly string firstExample;
        readonly string secondExample;

        public ScenarioThePromptForOneSponsor()
        {
            firstExample = ExamplePhonePattern.Match(AdScriptPromptBuilder.BuildSystemPrompt(Request("acme"))).Value;
            secondExample = ExamplePhonePattern.Match(AdScriptPromptBuilder.BuildSystemPrompt(Request("acme"))).Value;
        }

        /// <summary>AC1 — the same 555-01xx example both times</summary>
        [Fact]
        public void IsDeterministic() => Assert.Equal(("555-0115", "555-0115"), (firstExample, secondExample));
    }

    public sealed class ScenarioThePromptsForTwoSponsors
    {
        // Given: slugs "acme" and "zenith"
        readonly string acmeExample;
        readonly string zenithExample;

        public ScenarioThePromptsForTwoSponsors()
        {
            acmeExample = AdScriptPromptBuilder.ExamplePhone("acme");
            zenithExample = AdScriptPromptBuilder.ExamplePhone("zenith");
        }

        /// <summary>AC2 — different examples (pinned pair)</summary>
        [Fact]
        public void DiffersPerSponsor() => Assert.Equal(("555-0115", "555-0129"), (acmeExample, zenithExample));
    }

    public sealed class ScenarioAScriptWithTheExample
    {
        // Given: "555-0142" in the script, sponsor phone "812-555-0199", AdScriptWriter hygiene (T550)

        /// <summary>AC3 — the sponsor phone is present</summary>
        [Fact(Skip = PendingT550)]
        public void SaysTheSponsorPhone() => Assert.Fail(PendingT550);

        /// <summary>AC3 — no 555-0142 remains</summary>
        [Fact(Skip = PendingT550)]
        public void DropsTheExample() => Assert.Fail(PendingT550);
    }

    public sealed class ScenarioASponsorWhoseOwnPhoneIs555
    {
        // Given: sponsor phone "555-0100", script says it

        /// <summary>AC4 — unchanged</summary>
        [Fact(Skip = PendingT550)]
        public void KeepsTheSponsorsOwnFiveFiveFive() => Assert.Fail(PendingT550);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASponsorWithNoPhone
    {
        // Given: no phone; line "Call 555-0142 today for a quote."

        /// <summary>AC5 — the clause is dropped</summary>
        [Fact(Skip = PendingT550)]
        public void DropsTheClause() => Assert.Fail(PendingT550);

        /// <summary>AC5 — no digits remain</summary>
        [Fact(Skip = PendingT550)]
        public void LeavesNoDigits() => Assert.Fail(PendingT550);
    }

    public sealed class ScenarioAStrayNumberAfterHygiene
    {
        // Given: post-hygiene script carrying "555-0199" ≠ sponsor phone, F160 validation

        /// <summary>AC6 — the phone rule fails</summary>
        [Fact(Skip = PendingT550)]
        public void StillFailsTheValidator() => Assert.Fail(PendingT550);
    }

}
