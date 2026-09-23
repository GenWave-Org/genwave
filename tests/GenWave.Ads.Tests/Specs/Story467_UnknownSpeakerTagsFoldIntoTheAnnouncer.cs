// STORY-467 — Unknown speaker tags fold into the announcer (gh-#742 · SPEC F200 · PLAN T551)
//
// BDD specification — xUnit. GREEN: AdScriptParser.Parse folds any TagPattern-shaped tag that is not
// in the known cast (ANNOUNCER/VOICE1/VOICE2) onto ANNOUNCER, keeping the line's copy, and records one
// AdScript.Notes entry per distinct unknown tag. AC5 (the API's own parseNotes field) is specced
// separately in GenWave.Host.Tests/Specs/Story467_ParseNotesReachTheAdDetail.cs.

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureUnknownspeakertagsfoldintotheannouncer
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // STORY-467 ruling: AC1's own wording writes the second tag as "VOICE", shorthand for the cast tag
    // VOICE1 — the known cast is exactly ANNOUNCER/VOICE1/VOICE2 (AdCastPicker.Voice1Tag/Voice2Tag;
    // AdScriptPromptBuilder tells the writing model those three), so this fixture spells it VOICE1
    // verbatim rather than adding a bare "VOICE" to the cast.
    const string NarratorLineScript =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by tonight.\n" +
        "NARRATOR: In a world of ordinary diners...";

    static readonly AdScriptValidationRequest DefaultRequest = new(
        Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAScriptWithANarratorLine
    {
        // Given: ANNOUNCER, VOICE1, NARRATOR lines through AdScriptParser (STORY-467 ruling above)
        readonly AdScriptValidationResult.Accepted accepted;

        public ScenarioAScriptWithANarratorLine()
        {
            var result = AdScriptParser.Parse(NarratorLineScript, maxLineChars: 200);
            accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        /// <summary>AC1 — the NARRATOR copy is attributed to ANNOUNCER</summary>
        [Fact]
        public void KeepsTheLine() =>
            Assert.Contains(
                accepted.Script.Lines,
                line => line.Tag == AdScriptParser.AnnouncerTag && line.Text == "In a world of ordinary diners...");

        /// <summary>AC2 — note "unknown-tag:NARRATOR"</summary>
        [Fact]
        public void NamesTheTag() =>
            Assert.Contains("unknown-tag:NARRATOR", accepted.Script.Notes);

        /// <summary>AC3 — distinct voice count is 2</summary>
        [Fact]
        public void CountsKnownTagsOnly() =>
            Assert.Equal(2, accepted.Script.Lines.Select(line => line.Tag).Distinct().Count());
    }

    public sealed class ScenarioAScriptThatIsAllNarrator
    {
        // Given: every line NARRATOR
        const string Script =
            "NARRATOR: In a world of ordinary diners...\n" +
            "NARRATOR: One stands apart.";

        readonly AdScriptValidationResult result;

        public ScenarioAScriptThatIsAllNarrator()
        {
            result = AdScriptParser.Parse(Script, maxLineChars: 200);
        }

        /// <summary>AC4 — a valid one-voice script</summary>
        [Fact]
        public void IsOneVoice()
        {
            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            Assert.Equal(["ANNOUNCER", "ANNOUNCER"], accepted.Script.Lines.Select(line => line.Tag));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheValidator
    {
        // Given: the script of AC1 through F160 (AdScriptValidator.Validate, not the bare parser)
        readonly AdScriptValidationResult result;

        public ScenarioTheValidator()
        {
            result = AdScriptValidator.Validate(NarratorLineScript, DefaultRequest, new FakePatterDurationEstimator());
        }

        /// <summary>AC6 — no rule fails for the tag</summary>
        [Fact]
        public void IsNotAValidatorFailure() =>
            Assert.IsType<AdScriptValidationResult.Accepted>(result);
    }
}
