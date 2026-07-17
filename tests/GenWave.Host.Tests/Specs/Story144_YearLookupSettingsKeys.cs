// STORY-144 — Missing release years filled from MusicBrainz (Epic X / SPEC F48.5, closes gitea-#208) —
// settings keys half. The client half lives in MediaLibrary.Tests/Specs/Story144_MusicBrainzYearLookup.cs;
// the pipeline half in MediaLibrary.Tests/Specs/Story144_YearLookupPipeline.cs.
//
// BDD specification — xUnit. Implemented X5 (2026-07-14): Library:YearLookup:Enabled/Endpoint join
// the allowlist as LIVE (mirrors Tts:Endpoint/Llm:Endpoint's own F36.2 typed-client shape);
// Library:YearLookup:MinScore joins as ENRICHMENT apply-mode (F44.3 badge, alongside the CueDetection/
// Energy pair); all three carry a SettingValidator entry. Idiom mirrors Story139_SettingsSurfaceCompletion
// (V8) exactly.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureYearLookupSettingsKeys
{
    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Minimal defaults for the three F48.5 keys — enough for SettingValidator to validate a batch
    // without touching the whole allowlist (mirrors Story139's NewKeyDefaults()).
    static IEnumerable<KeyValuePair<string, string?>> KeyDefaults() =>
    [
        new("Library:YearLookup:Enabled",  "true"),
        new("Library:YearLookup:Endpoint", "https://musicbrainz.org/ws/2"),
        new("Library:YearLookup:MinScore", "90"),
    ];

    public sealed class ScenarioTheKnobsJoinTheAllowlist
    {
        [Fact]
        public void EnabledAndEndpointBadgeLive()
        {
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Library:YearLookup:Enabled", out var enabled));
            Assert.Equal(SettingApplyMode.Live, enabled.ApplyMode);

            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Library:YearLookup:Endpoint", out var endpoint));
            Assert.Equal(SettingApplyMode.Live, endpoint.ApplyMode);
        }

        [Fact]
        public void MinScoreBadgesEnrichment()
        {
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Library:YearLookup:MinScore", out var minScore));
            Assert.Equal(SettingApplyMode.Enrichment, minScore.ApplyMode);
        }

        [Fact]
        public void TheKeysLandInTheLibrarySettingsSection()
        {
            // Server-driven section metadata: the admin UI's sectionForKey groups by the
            // "Library:" prefix (SPEC F44.8) — pinned here at the allowlist-key level (this is the
            // Host half; the UI's own prefix rule is unchanged by X5 and needs no admin-ui edit).
            // Reads the key back from the allowlist entry itself, not a literal, so this fact
            // actually exercises StationSettingsAllowlist rather than restating its own input.
            string[] keys =
            [
                "Library:YearLookup:Enabled",
                "Library:YearLookup:Endpoint",
                "Library:YearLookup:MinScore",
            ];

            foreach (var key in keys)
            {
                Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(key, out var allowed),
                    $"{key} is missing from the allowlist");
                Assert.StartsWith("Library:", allowed.Key, StringComparison.Ordinal);
            }
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioInvalidValuesAreRejected
    {
        [Fact]
        public void ANonBooleanEnabledIsRejected()
        {
            var validator = new SettingValidator(BuildConfig(KeyDefaults()));

            Assert.Null(validator.Validate("Library:YearLookup:Enabled", "true"));
            Assert.NotNull(validator.Validate("Library:YearLookup:Enabled", "not-a-bool"));
        }

        [Fact]
        public void AMalformedEndpointUrlIsRejected()
        {
            var validator = new SettingValidator(BuildConfig(KeyDefaults()));

            Assert.Null(validator.Validate("Library:YearLookup:Endpoint", "https://musicbrainz.org/ws/2"));
            Assert.NotNull(validator.Validate("Library:YearLookup:Endpoint", "not-a-url"));
            Assert.NotNull(validator.Validate("Library:YearLookup:Endpoint", ""));
        }

        [Fact]
        public void AnOutOfRangeMinScoreIsRejected()
        {
            var validator = new SettingValidator(BuildConfig(KeyDefaults()));

            Assert.Null(validator.Validate("Library:YearLookup:MinScore", "90"));
            Assert.Null(validator.Validate("Library:YearLookup:MinScore", "0"));
            Assert.Null(validator.Validate("Library:YearLookup:MinScore", "100"));
            Assert.NotNull(validator.Validate("Library:YearLookup:MinScore", "-1"));
            Assert.NotNull(validator.Validate("Library:YearLookup:MinScore", "101"));
        }
    }
}
