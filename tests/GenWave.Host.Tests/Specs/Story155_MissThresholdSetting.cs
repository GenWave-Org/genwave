// STORY-155 — A mount blip never sidelines the library (Epic Z / SPEC F58, closes gitea-#223) —
// the settings-surface half: Library:Scan:MissThreshold is a live setting.
//
// BDD specification — xUnit. Implemented Z2 (2026-07-15). The key joins the allowlist (F19/F44),
// SettingValidator bounds it (floor 1, ceiling 20 — F53.1 grows one row), and the Y6 parity
// discipline forces help text to land with the key (the jest settings-help-coverage spec goes red
// if Z2 adds the key without copy). Idiom mirrors Story149_SettingCeilings.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMissThresholdSetting
{
    const string Key = "Library:Scan:MissThreshold";

    /// <summary>Repo root, resolved relative to the test assembly's build output — the Story074/
    /// Story102/Story107/Story151 RepoRoot convention for reaching repo-root files from a test project.</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — a live, bounded knob
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioTheThresholdIsALiveSetting
    {
        [Fact]
        public void TheKeyIsAllowlisted()
        {
            Assert.True(StationSettingsAllowlist.ByKey.ContainsKey(Key));

            var allowed = StationSettingsAllowlist.ByKey[Key];
            Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            Assert.Equal(SettingKind.Number, allowed.Kind);
        }

        [Fact]
        public void ValuesAtFloorAndCeilingAreAcceptedInclusive()
        {
            var validator = BuildValidator();

            Assert.Null(validator.Validate(Key, "1"));
            Assert.Null(validator.Validate(Key, "20"));
        }

        [Fact]
        public void TheDefaultResolvesToTwoOnAFreshDeploy()
        {
            // The real, on-disk appsettings.json a fresh deploy actually loads — not an in-memory
            // stand-in. Mirrors the F55.1/Story151 seeded-defaults discipline: the C#
            // ScanOptions.MissThreshold default of 2 must surface as a real configured value, not an
            // invisible property initializer invisible to IConfiguration (the gitea-#231 root cause).
            var config = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();

            Assert.Equal("2", config[Key]);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioOutOfBandValuesAreRejected
    {
        [Fact]
        public void ZeroRejectsWithBothBoundsNamed()
        {
            var validator = BuildValidator();

            var error = validator.Validate(Key, "0");

            Assert.NotNull(error);
            Assert.Contains("1", error);
            Assert.Contains("20", error);
        }

        [Fact]
        public void TwentyOneRejectsWithBothBoundsNamed()
        {
            var validator = BuildValidator();

            var error = validator.Validate(Key, "21");

            Assert.NotNull(error);
            Assert.Contains("1", error);
            Assert.Contains("20", error);
        }
    }
}
