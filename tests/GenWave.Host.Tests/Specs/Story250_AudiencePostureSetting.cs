// STORY-250 — A station that knows its audience: the setting + the end-to-end guarantee
// (gh-#174, SPEC F95.1, F95.6, PLAN T111/T114)
//
// BDD specification — xUnit. Entry-point discipline: the setting facts drive the
// real settings API; the F95.6 pins ride a real playout run — nothing explicit airs on an
// everyone station, therefore nothing explicit reaches ICY, spectator, or the DJ's mouth.
// Companion to Story250_ExplicitPoolExclusion.cs (MediaLibrary.Tests).
//
// T111 implements ScenarioTheSettingExists: the allowlist entry, the StationOptions.Audience
// property (seeded "everyone" in appsettings.json — the F55.1/Story151 discipline), and the
// SettingValidator guard (case-insensitive, mirroring Llm:DegradationPin). No consumer reads the
// setting yet — T114 wires the shared pool predicate ScenarioNothingExplicitReachesAnySurface
// pins against a real playout run.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAudiencePostureSetting
{
    const string Key = "Station:Audience";

    /// <summary>Repo root, resolved relative to the test assembly's build output — the
    /// Story074/Story102/Story107/Story151/Story155 convention for reaching repo-root files from
    /// a test project.</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    public sealed class ScenarioTheSettingExists
    {
        // Given a fresh station (F95.1).

        [Fact]
        public void DefaultIsEveryone()
        {
            // The real, on-disk appsettings.json a fresh deploy actually loads — mirrors the
            // F55.1/Story151 seeded-defaults discipline: StationOptions.Audience's C# default
            // must surface as a real configured value, not an invisible property initializer
            // invisible to IConfiguration (the gitea-#231 root cause).
            var config = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();

            Assert.Equal(new StationOptions().Audience, config[Key]);
            Assert.Equal("everyone", config[Key]);
        }

        [Fact]
        public void KeyIsAllowlistedAndLiveApply()
        {
            Assert.True(StationSettingsAllowlist.ByKey.ContainsKey(Key));

            var allowed = StationSettingsAllowlist.ByKey[Key];
            Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            Assert.Equal(SettingKind.String, allowed.Kind);
        }

        [Fact]
        public void OnlyEveryoneOrMatureAreAccepted()
        {
            var validator = BuildValidator();

            Assert.Null(validator.Validate(Key, "everyone"));
            Assert.Null(validator.Validate(Key, "mature"));

            // Case-insensitive, mirroring Llm:DegradationPin's own guard.
            Assert.Null(validator.Validate(Key, "EVERYONE"));
            Assert.Null(validator.Validate(Key, "Mature"));

            Assert.NotNull(validator.Validate(Key, "explicit"));
            Assert.NotNull(validator.Validate(Key, ""));
            Assert.NotNull(validator.Validate(Key, "everyone,mature"));
        }
    }

    public sealed class ScenarioNothingExplicitReachesAnySurface
    {
        // Given a stamped-explicit track on an everyone station, When a real playout run
        // completes (F95.6) — enforcement upstream of every display surface.

        [Fact(Skip = "Pending (T114)")]
        public void TheTrackNeverEntersAPick() { }

        [Fact(Skip = "Pending (T114)")]
        public void FlippedToMatureLiveTheTrackBecomesEligible() { }
    }
}
