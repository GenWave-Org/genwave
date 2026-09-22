// STORY-464 — Phone shape lives in Core (gh-#700 · SPEC F197.1 · PLAN T544)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Core.Tests.Specs;

public static class FeaturePhoneshapelivesinCore
{
    const string Pending = "pending: T544 — Phone shape lives in Core (STORY-464)";

    public sealed class ScenarioTheFourNanpShapes
    {
        // Given: GenWave.Core.PhoneShape against "555-0142", "812-555-0199", "(812) 555-0199", "812.555.0199"

        /// <summary>AC1 — seven-digit dash</summary>
        [Fact(Skip = Pending)]
        public void MatchesSevenDigits() => Assert.Fail(Pending);

        /// <summary>AC1 — ten-digit dash</summary>
        [Fact(Skip = Pending)]
        public void MatchesTenDigits() => Assert.Fail(Pending);

        /// <summary>AC1 — parenthesised area code</summary>
        [Fact(Skip = Pending)]
        public void MatchesParenthesisedAreaCode() => Assert.Fail(Pending);

        /// <summary>AC1 — dotted</summary>
        [Fact(Skip = Pending)]
        public void MatchesDottedSeparators() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheAdsAssembly
    {
        // Given: reflection over GenWave.Ads for Regex literals containing "\d{3}"

        /// <summary>AC2 — none remain</summary>
        [Fact(Skip = Pending)]
        public void KeepsNoPhoneRegexOfItsOwn() => Assert.Fail(Pending);
    }

}
