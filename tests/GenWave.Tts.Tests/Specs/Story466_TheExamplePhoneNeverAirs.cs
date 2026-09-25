// STORY-466 — The example phone never airs (gh-#701 · SPEC F199 · PLAN T549 T550)
//
// BDD specification — xUnit. AC1/AC2 (T549) and AC3–AC5 (T550, AdScriptWriter.ApplyPhoneHygiene) live
// here. AC6 needs the real GenWave.Ads.AdScriptValidator — GenWave.Tts.Tests does not (and must not)
// reference GenWave.Ads — so it lives in
// tests/GenWave.Ads.Tests/Specs/Story466_TheValidatorStillRefusesAStray555.cs instead.

namespace GenWave.Tts.Tests.Specs;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

public static class FeatureTheexamplephoneneverairs
{
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
        const string Script = "ANNOUNCER: Call 555-0142 for a free quote.";
        const string SponsorPhone = "812-555-0199";

        readonly string result;

        public ScenarioAScriptWithTheExample() => result = AdScriptWriter.ApplyPhoneHygiene(Script, SponsorPhone);

        /// <summary>AC3 — the sponsor phone is present</summary>
        [Fact]
        public void SaysTheSponsorPhone() => Assert.Contains(SponsorPhone, result, StringComparison.Ordinal);

        /// <summary>AC3 — no 555-0142 remains</summary>
        [Fact]
        public void DropsTheExample() => Assert.DoesNotContain("555-0142", result, StringComparison.Ordinal);
    }

    public sealed class ScenarioASponsorWhoseOwnPhoneIs555
    {
        // Given: sponsor phone "555-0100", script says it
        const string Script = "ANNOUNCER: Call 555-0100 today.";

        readonly string result;

        public ScenarioASponsorWhoseOwnPhoneIs555() => result = AdScriptWriter.ApplyPhoneHygiene(Script, "555-0100");

        /// <summary>AC4 — unchanged</summary>
        [Fact]
        public void KeepsTheSponsorsOwnFiveFiveFive() => Assert.Equal(Script, result);
    }

    public sealed class ScenarioAParenFormattedSponsorPhone
    {
        // Given: sponsor phone "(406) 222-0100", script already carrying it in the same paren format
        const string Script = "ANNOUNCER: Call (406) 222-0100 today.";

        readonly string result;

        public ScenarioAParenFormattedSponsorPhone() =>
            result = AdScriptWriter.ApplyPhoneHygiene(Script, "(406) 222-0100");

        /// <summary>AC4 — the paren-formatted sponsor phone is left exactly as written, never
        /// misread as only its own trailing 7-digit tail</summary>
        [Fact]
        public void KeepsTheParenFormattedNumber() => Assert.Equal(Script, result);
    }

    public sealed class ScenarioTheSponsorPhoneInADifferentFormat
    {
        // Given: sponsor phone "812-555-0199", script carries the SAME digits wrapped in parens instead
        const string Script = "ANNOUNCER: Call (812) 555-0199 today.";

        readonly string result;

        public ScenarioTheSponsorPhoneInADifferentFormat() =>
            result = AdScriptWriter.ApplyPhoneHygiene(Script, "812-555-0199");

        /// <summary>AC4 — digit equality clears the run even when the formatting differs</summary>
        [Fact]
        public void KeepsTheDifferentlyFormattedNumber() => Assert.Equal(Script, result);
    }

    public sealed class ScenarioAStrayNumberIsReplacedAsOneWholeRun
    {
        // Given: sponsor phone "(406) 222-0100", a stray dashed number sharing none of its digits
        const string Script = "ANNOUNCER: Call 812-555-0199.";

        readonly string result;

        public ScenarioAStrayNumberIsReplacedAsOneWholeRun() =>
            result = AdScriptWriter.ApplyPhoneHygiene(Script, "(406) 222-0100");

        /// <summary>AC3 — the whole 10-digit run is replaced, never split into a 7-digit tail</summary>
        [Fact]
        public void ReplacesTheWholeRun() => Assert.Equal("ANNOUNCER: Call (406) 222-0100.", result);
    }

    public sealed class ScenarioAParenthesizedExampleIsReplaced
    {
        // gh-#856: PhoneShapedRunPattern gained the same "(ddd) dddd" alternative as
        // GenWave.Core.PhoneShape — a stray "(555) 0142" (the exchange itself parenthesized, no
        // third digit group) must be caught and replaced too, not just its dashed sibling above.
        const string Script = "ANNOUNCER: Call (555) 0142 today!";
        const string SponsorPhone = "812-555-0199";

        readonly string result;

        public ScenarioAParenthesizedExampleIsReplaced() =>
            result = AdScriptWriter.ApplyPhoneHygiene(Script, SponsorPhone);

        /// <summary>AC3 — the sponsor phone replaces the whole parenthesized run</summary>
        [Fact]
        public void ReplacesTheWholeRun() => Assert.Equal("ANNOUNCER: Call 812-555-0199 today!", result);
    }

    public sealed class ScenarioAParenthesizedExampleWithADashSeparatorIsReplaced
    {
        // gh-#856: same run, dash separator.
        const string Script = "ANNOUNCER: Call (555)-0142 today!";
        const string SponsorPhone = "812-555-0199";

        readonly string result;

        public ScenarioAParenthesizedExampleWithADashSeparatorIsReplaced() =>
            result = AdScriptWriter.ApplyPhoneHygiene(Script, SponsorPhone);

        /// <summary>AC3 — the sponsor phone replaces the whole parenthesized run</summary>
        [Fact]
        public void ReplacesTheWholeRun() => Assert.Equal("ANNOUNCER: Call 812-555-0199 today!", result);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASponsorWithNoPhone
    {
        // Given: no phone; line "Call 555-0142 today for a quote." (F199.4 pinned)
        readonly string result;

        public ScenarioASponsorWithNoPhone() =>
            result = AdScriptWriter.ApplyPhoneHygiene("ANNOUNCER: Call 555-0142 today for a quote.", sponsorPhone: null);

        /// <summary>AC5 — the clause is dropped</summary>
        [Fact]
        public void DropsTheClause() => Assert.Equal("ANNOUNCER: Today for a quote.", result);

        /// <summary>AC5 — no digits remain</summary>
        [Fact]
        public void LeavesNoDigits() => Assert.DoesNotContain(result, char.IsDigit);
    }

    public sealed class ScenarioAParenthesizedExampleWithNoSponsorPhone
    {
        // gh-#856: same AC5 drop, but the run is the parenthesized "(555) 0142" shape.
        readonly string result;

        public ScenarioAParenthesizedExampleWithNoSponsorPhone() =>
            result = AdScriptWriter.ApplyPhoneHygiene("ANNOUNCER: Call (555) 0142 today!", sponsorPhone: null);

        /// <summary>AC5 — the clause is dropped</summary>
        [Fact]
        public void DropsTheClause() => Assert.Equal("ANNOUNCER: Today!", result);
    }

    public sealed class ScenarioTheWholeLineIsThePhoneClause
    {
        // Given: no phone; the line has nothing in it but the phone clause
        readonly string result;

        public ScenarioTheWholeLineIsThePhoneClause() =>
            result = AdScriptWriter.ApplyPhoneHygiene("ANNOUNCER: Call us at 555-0142.", sponsorPhone: null);

        /// <summary>AC5 — the line is dropped entirely, never left as a bare orphaned "."</summary>
        [Fact]
        public void DropsTheWholeLine() => Assert.Equal(string.Empty, result);
    }

    public sealed class ScenarioThePhoneClauseFollowsAComma
    {
        // Given: no phone; a comma-joined lead-in precedes the phone clause
        readonly string result;

        public ScenarioThePhoneClauseFollowsAComma() =>
            result = AdScriptWriter.ApplyPhoneHygiene("ANNOUNCER: Cravin's Diner, 555-0142.", sponsorPhone: null);

        /// <summary>AC5 — the orphaned comma goes with the clause; the sentence's own period stays</summary>
        [Fact]
        public void LeavesNoOrphanPunctuation() => Assert.Equal("ANNOUNCER: Cravin's Diner.", result);
    }
}
