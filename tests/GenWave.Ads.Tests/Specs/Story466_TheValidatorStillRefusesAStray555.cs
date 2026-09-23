// STORY-466 — The example phone never airs (validator half: AC6 · SPEC F199.3 · PLAN T550)
//
// AC1–AC5 live in tests/GenWave.Tts.Tests/Specs/Story466_TheExamplePhoneNeverAirs.cs (AC3–AC5 drive
// AdScriptWriter.ApplyPhoneHygiene directly). AC6 needs the REAL GenWave.Ads.AdScriptValidator — a
// hygiene bug (or a script that skipped hygiene entirely, e.g. an owner-typed save) must still be
// caught here, never silently aired — and GenWave.Tts.Tests does not (and must not) reference
// GenWave.Ads, so this one fact lives on this side of the L10 boundary instead.

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureTheValidatorStillRefusesAStray555
{
    static readonly AdScriptValidationRequest Request = new(
        Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4,
        SponsorPhone: "812-555-0142", IsPackOwned: false);

    public sealed class ScenarioAStrayNumberAfterHygiene
    {
        // Given: a post-hygiene script carrying "555-0199" — not the sponsor's own "812-555-0142" on
        // file — and F160 validation (SPEC F199.3: a real sponsor phone tightens the 555 skip to ONLY
        // that exact number, so a differing run refuses even though it itself contains "555")
        const string Script =
            "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
            "ANNOUNCER: Call 555-0199 today.";

        readonly AdScriptValidationResult result =
            AdScriptValidator.Validate(Script, Request, new FakePatterDurationEstimator());

        /// <summary>AC6 — the phone rule fails</summary>
        [Fact]
        public void StillFailsTheValidator()
        {
            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }
    }
}
