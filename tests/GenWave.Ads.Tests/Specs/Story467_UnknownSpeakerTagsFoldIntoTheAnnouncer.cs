// STORY-467 — Unknown speaker tags fold into the announcer (gh-#742 · SPEC F200 · PLAN T551)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Ads.Tests.Specs;

public static class FeatureUnknownspeakertagsfoldintotheannouncer
{
    const string Pending = "pending: T551 — Unknown speaker tags fold into the announcer (STORY-467)";

    public sealed class ScenarioAScriptWithANarratorLine
    {
        // Given: ANNOUNCER, VOICE, NARRATOR lines through AdScriptParser

        /// <summary>AC1 — the NARRATOR copy is attributed to ANNOUNCER</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheLine() => Assert.Fail(Pending);

        /// <summary>AC2 — note "unknown-tag:NARRATOR"</summary>
        [Fact(Skip = Pending)]
        public void NamesTheTag() => Assert.Fail(Pending);

        /// <summary>AC3 — distinct voice count is 2</summary>
        [Fact(Skip = Pending)]
        public void CountsKnownTagsOnly() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAScriptThatIsAllNarrator
    {
        // Given: every line NARRATOR

        /// <summary>AC4 — a valid one-voice script</summary>
        [Fact(Skip = Pending)]
        public void IsOneVoice() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheValidator
    {
        // Given: the script of AC1 through F160

        /// <summary>AC6 — no rule fails for the tag</summary>
        [Fact(Skip = Pending)]
        public void IsNotAValidatorFailure() => Assert.Fail(Pending);
    }

}
