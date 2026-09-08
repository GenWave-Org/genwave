// STORY-402 — AC1: the cast-pool settings live on the operator-editable allowlist (SPEC F167.1 · PLAN T415 review R9)
//
// Moved here from GenWave.Ads.Tests/Specs/Story402_AdCastPicker.cs (same fact names, same outer
// scenario name) — StationSettingsAllowlist is a GenWave.Host type, and GenWave.Ads.Tests never
// references GenWave.Host (L10), so a fact asserting against the allowlist directly can only live
// in this project. Story405_AdsSettingsSplit.cs (this same directory) already proves
// AnnouncerVoice/CastVoices round-trip through the live PUT endpoint end to end (T417) — this file's
// own, narrower job is the allowlist ENTRY itself plus the seeded CastVoices default's own "never
// sounds like an existing DJ" promise (the allowlist's own remarks, immediately above these entries).

using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdCastPickerBuildsAVoicePlan
{
    /// <summary>The real, on-disk <c>src/GenWave.Host/appsettings.json</c> — the Story151_SeededDefaults
    /// convention (that file's own remarks), applied here so the "four stock ids" fact below actually
    /// reads the SHIPPED default rather than a hand-typed literal that could silently drift from it
    /// (PLAN T415 review F3).</summary>
    static IConfiguration RealAppSettingsConfig() =>
        new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "src", "GenWave.Host", "appsettings.json"),
                optional: false)
            .Build();

    public sealed class ScenarioSettingsLiveOnTheAllowlist
    {
        [Fact]
        public void AdsAnnouncerVoiceAppearsInTheAllowlistWithTheDocumentedDefault()
        {
            // Given the allowlist, When Station:Ads:AnnouncerVoice is looked up...
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue("Station:Ads:AnnouncerVoice", out var setting));

            // Then it is Live (a PUT reaches the very next tick's cast pick, no restart) and a bare
            // string — appsettings.json's own default is "" (unset means "use the station voice",
            // AdLiveSettingsReader's own remarks).
            Assert.Equal(SettingApplyMode.Live, setting!.ApplyMode);
            Assert.Equal(SettingKind.String, setting.Kind);
        }

        [Fact]
        public void AdsCastVoicesAppearsInTheAllowlistSeededWithFourStockIds()
        {
            // Given the allowlist, When Station:Ads:CastVoices is looked up...
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue("Station:Ads:CastVoices", out var setting));
            Assert.Equal(SettingApplyMode.Live, setting!.ApplyMode);
            Assert.Equal(SettingKind.String, setting.Kind);

            // Then the REAL on-disk appsettings.json default (what AdLiveSettingsReader falls back to
            // before any operator PUT — read off disk, not a hand-typed literal here, so a drift in
            // the shipped file actually moves this fact, PLAN T415 review F3) is four stock Kokoro
            // voice ids, pairwise distinct and distinct from every station-voice default this repo
            // actually ships. Deviation from the allowlist's own comment ("distinct from every
            // persona catalog default"): no in-repo persona-catalog-defaults list exists to check
            // against, and production appsettings.json carries no Station:Voice key at all — af_heart
            // (appsettings.Development.json) is the only shipped station-voice default anywhere in
            // this repo, so it is the narrowest honest check available (PLAN T415 review R9).
            var raw = RealAppSettingsConfig()["Station:Ads:CastVoices"];
            Assert.NotNull(raw);
            Assert.False(string.IsNullOrWhiteSpace(raw));
            var defaults = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(4, defaults.Length);
            Assert.Equal(defaults.Distinct(StringComparer.Ordinal).Count(), defaults.Length);
            Assert.DoesNotContain("af_heart", defaults);
        }
    }
}
