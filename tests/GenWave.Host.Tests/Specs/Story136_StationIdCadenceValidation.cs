// STORY-136 — Station IDs: no boot blast, a real off switch (Epic V / SPEC F42.2, closes gitea-#216) —
// validation half. The orchestrator half lives in
// Orchestration.Tests/Specs/Story136_StationIdCadence.cs.
//
// BDD specification — xUnit. Implemented V6 (2026-07-14): the Range floor on
// StationCadenceOptions.StationIdEveryNUnits widens 1 -> 0, and SettingValidator's entry for
// Station:Cadence:StationIdEveryNUnits switches from IsPositiveInt to IsNonNegativeInt (the same
// predicate the Rotation knobs already use for their own "0 disables" floor, F41.6).
//
// V6 review fix: the original "boot validation" facts here exercised the DataAnnotations
// [Range] on StationCadenceOptions in isolation via Validator.TryValidateObject — a path
// production never runs. Program.cs's `.ValidateDataAnnotations()` is chained on the ROOT
// StationOptions and does not recurse into nested option classes, so that attribute was dead
// at the real boot surface (appsettings Station:Cadence:StationIdEveryNUnits=-1 booted
// successfully). The Story009_StationContextConfigBinding idiom this originally claimed to
// mirror does not transfer to nested classes. The facts below instead exercise the seam that
// actually runs at ValidateOnStart: StationOptionsValidator.Validate, called directly against
// a fully-populated valid StationOptions (mirrors the ValidOptions() fixture used by
// Story074_SafeAuthoringConfigContractAndVolume's ScenarioInvalidSafeValuesFailAtBoot). The
// [Range] attribute stays on StationCadenceOptions as documentation only.
//
// Same review pass flagged the identical dead-attribute shape on StationRotationOptions
// (RecentWindow, ArtistSeparation) — StationOptionsValidator now guards both, proven below
// alongside the cadence facts.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationIdCadenceValidation
{
    const string Key = "Station:Cadence:StationIdEveryNUnits";

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

    // SettingValidator.Validate never reads the injected IConfiguration for this key (only
    // ValidateBatch's cross-field xfade check does), so an empty configuration is enough here.
    static SettingValidator BuildSettingValidator() =>
        new(new ConfigurationBuilder().Build());

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioZeroIsLegalAtBothSurfaces
    {
        [Fact]
        public void BootValidationAcceptsZero()
        {
            var options = ValidOptions();
            options.Cadence.StationIdEveryNUnits = 0;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void SettingValidatorAcceptsZero() =>
            Assert.Null(BuildSettingValidator().Validate(Key, "0"));

        [Fact]
        public void TheDefaultRemainsFour() =>
            Assert.Equal(4, new StationCadenceOptions().StationIdEveryNUnits);
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioNegativeValuesStillFailAtBothSurfaces
    {
        [Fact]
        public void BootValidationRejectsMinusOne()
        {
            var options = ValidOptions();
            options.Cadence.StationIdEveryNUnits = -1;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:Cadence:StationIdEveryNUnits",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void SettingValidatorRejectsMinusOne() =>
            Assert.NotNull(BuildSettingValidator().Validate(Key, "-1"));
    }

    // ---------------------------------------------------------------------
    // Rotation floors — same dead-attribute shape, same validator, same review pass
    // ---------------------------------------------------------------------

    public sealed class ScenarioRotationFloorsAreLegalAtZero
    {
        [Fact]
        public void RecentWindowZeroPassesBootValidation()
        {
            var options = ValidOptions();
            options.Rotation.RecentWindow = 0;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void ArtistSeparationZeroPassesBootValidation()
        {
            var options = ValidOptions();
            options.Rotation.ArtistSeparation = 0;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Succeeded);
        }
    }

    public sealed class ScenarioRotationFloorsRejectNegativeValues
    {
        [Fact]
        public void RecentWindowMinusOneFailsBootValidation()
        {
            var options = ValidOptions();
            options.Rotation.RecentWindow = -1;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:Rotation:RecentWindow",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void ArtistSeparationMinusOneFailsBootValidation()
        {
            var options = ValidOptions();
            options.Rotation.ArtistSeparation = -1;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:Rotation:ArtistSeparation",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }
    }
}
