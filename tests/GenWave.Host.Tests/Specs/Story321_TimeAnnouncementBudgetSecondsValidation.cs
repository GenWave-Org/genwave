// STORY-321 — A late time check dies quietly (gh-#469 · SPEC F124.4 · PLAN T269) — the Host-side
// floor half. The queue/orchestrator half lives in
// Orchestration.Tests/Specs/Story321_LateTimeCheckDies.cs.
//
// BDD specification — xUnit. Round-3 review finding F1: Station:Imaging:TimeAnnouncementBudgetSeconds
// must fail boot at 0 (0 would drop EVERY TimeDate deferral undrained, silently killing F110.3,
// rather than disabling anything the way every "0 = off" knob elsewhere in StationOptionsValidator
// does) — the [Range(1, int.MaxValue)] on StationImagingOptions is documentation-only (Program.cs's
// ValidateDataAnnotations() does not recurse into nested option classes, the Story136 precedent),
// so StationOptionsValidator.Validate is the seam that actually enforces this at ValidateOnStart.
// Mirrors Story136_StationIdCadenceValidation.cs's own BuildStationOptionsValidator/ValidOptions
// idiom.
//
// Retargeted at PLAN T326 (SPEC F141.1): the key/shape widened from Station:Imaging:
// TimeAnnouncementStaleMinutes (minutes, default 5) to Station:Imaging:TimeAnnouncementBudgetSeconds
// (seconds, default 420) — gh-#526's field data showed every real overrun landing just past the old
// 300s ceiling. The floor/ceiling shape this file pins is otherwise byte-identical to the F124.4
// original; only the key, unit, and default number changed.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTimeAnnouncementBudgetSecondsValidation
{
    const string Key = "Station:Imaging:TimeAnnouncementBudgetSeconds";

    /// <summary>A minimally-valid StationOptions instance for direct validator construction.</summary>
    static StationOptions ValidOptions() => new()
    {
        Id    = "s1",
        Name  = "GenWave",
        Voice = "af_heart",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
    };

    static StationOptionsValidator BuildStationOptionsValidator() =>
        new(NullLogger<StationOptionsValidator>.Instance);

    static SettingValidator BuildSettingValidator() =>
        new(new ConfigurationBuilder().Build());

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheShippedDefaultPassesBothSurfaces
    {
        [Fact]
        public void BootValidationAcceptsTheDefault()
        {
            var result = BuildStationOptionsValidator().Validate(null, ValidOptions());

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void SettingValidatorAcceptsFourTwenty() =>
            Assert.Null(BuildSettingValidator().Validate(Key, "420"));

        [Fact]
        public void TheDefaultRemainsFourTwenty() =>
            Assert.Equal(420, new StationImagingOptions().TimeAnnouncementBudgetSeconds);
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioZeroFailsBootUnlikeEveryOtherImagingCadenceKnob
    {
        [Fact]
        public void BootValidationRejectsZero()
        {
            var options = ValidOptions();
            options.Imaging.TimeAnnouncementBudgetSeconds = 0;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(Key, result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public void SettingValidatorRejectsZero() =>
            Assert.NotNull(BuildSettingValidator().Validate(Key, "0"));

        [Fact]
        public void BootValidationRejectsNegativeOne()
        {
            var options = ValidOptions();
            options.Imaging.TimeAnnouncementBudgetSeconds = -1;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
        }
    }
}
