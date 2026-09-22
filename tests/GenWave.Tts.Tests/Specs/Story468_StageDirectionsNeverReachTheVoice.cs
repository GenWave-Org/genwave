// STORY-468 — Stage directions never reach the voice (gh-#706 · SPEC F201 · PLAN T552)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureStagedirectionsneverreachthevoice
{
    const string Pending = "pending: T552 — Stage directions never reach the voice (STORY-468)";

    public sealed class ScenarioOneShapePerLine
    {
        // Given: "(warmly) Come on down today." · "Come on down [beat] today." · "Come on down *pause* today." through ApplyLineAwareHygiene

        /// <summary>AC1 — parentheses stripped</summary>
        [Fact(Skip = Pending)]
        public void StripsParentheses() => Assert.Fail(Pending);

        /// <summary>AC2 — brackets stripped</summary>
        [Fact(Skip = Pending)]
        public void StripsBrackets() => Assert.Fail(Pending);

        /// <summary>AC3 — asterisks stripped</summary>
        [Fact(Skip = Pending)]
        public void StripsAsterisks() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAllThreeOnOneLine
    {
        // Given: "(warmly) Come on down *pause* today [beat]."

        /// <summary>AC4 — "Come on down today."</summary>
        [Fact(Skip = Pending)]
        public void StripsEveryShape() => Assert.Fail(Pending);
    }

    public sealed class ScenarioALineThatIsOnlyADirection
    {
        // Given: "VOICE: (laughs)"

        /// <summary>AC5 — the VOICE line is gone</summary>
        [Fact(Skip = Pending)]
        public void DropsTheEmptiedLine() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioResidueAfterHygiene
    {
        // Given: post-hygiene text containing "(beat)" through F160

        /// <summary>AC6 — rule "stage-direction" names the line</summary>
        [Fact(Skip = Pending)]
        public void FailsValidation() => Assert.Fail(Pending);
    }

}
