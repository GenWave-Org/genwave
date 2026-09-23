// STORY-468 — Stage directions never reach the voice (validator half: AC6 · SPEC F201.2 · PLAN T552)
//
// AC1–AC5 live in tests/GenWave.Tts.Tests/Specs/Story468_StageDirectionsNeverReachTheVoice.cs (they
// drive AdScriptWriter.ApplyLineAwareHygiene directly). AC6 needs the REAL GenWave.Ads.AdScriptValidator
// — a hygiene bug, or a script that skipped hygiene entirely (e.g. an owner-typed save), must still be
// caught here, never silently aired — and GenWave.Tts.Tests does not (and must not) reference
// GenWave.Ads, so this one fact lives on this side of the L10 boundary instead.

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureResidueFailsValidation
{
    static readonly AdScriptValidationRequest Request =
        new(Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

    public sealed class ScenarioResidueAfterHygiene
    {
        // Given: post-hygiene script text still carrying "(beat)" — a shape ApplyLineAwareHygiene
        // should have already stripped, reaching this validator anyway (a hygiene bug, or a script
        // that never passed through hygiene at all).
        const string Script =
            "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
            "ANNOUNCER: Call now (beat) before it's gone.";

        readonly AdScriptValidationResult result =
            AdScriptValidator.Validate(Script, Request, new FakePatterDurationEstimator());

        /// <summary>AC6 — rule "stage_direction" names the line</summary>
        [Fact]
        public void FailsValidation()
        {
            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.StageDirection, refused.Violation.RuleId);
            Assert.Contains("line 2", refused.Violation.Reason, StringComparison.Ordinal);
            Assert.Contains("(beat)", refused.Violation.Reason, StringComparison.Ordinal);
        }
    }
}
