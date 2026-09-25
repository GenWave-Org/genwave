// STORY-477 — Descriptor law (gh-#778 · SPEC F205.6 · PLAN T575)
//
// BDD specification — xUnit. Reads what SHIPS, not the source file: every offender list is built
// off StationSettingsAllowlist.All (the compiled GenWave.Host.dll) and SettingsResources' own
// embedded neutral ResourceSet (via ResourceManager, never a textual read of the .resx — its
// schema-header XML COMMENT contains sample Name1/Color1/Bitmap1/Icon1 rows that a text scan would
// wrongly count as real entries).
using GenWave.Architecture.Tests.Support;
using GenWave.Host.Configuration;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureDescriptorlaw
{
    // Static-Choice keys whose labels come from live catalog/store data, never this resx (Station:Theme
    // from ThemeCatalog manifest names, Station:IconPack from installed pack slugs). Feature-level so
    // both scenarios below read the one set: ScenarioKeysAndCopy skips these when building AC12's
    // offender list, and ScenarioChoiceExemptions guards that every exempt key is still a real
    // static-Choice key.
    public static readonly IReadOnlySet<string> ChoiceLabelExemptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "Station:Theme",
        "Station:IconPack",
    };

    public sealed class ScenarioKeysAndCopy
    {
        // Given: AllowedSetting keys × SettingsResources.resx's shipped entries — arranged once
        // (a static field, not a constructor: xUnit builds a fresh class instance per [Fact], and
        // every fact here reads the same four immutable offender lists) so each fact stays a bare
        // Assert.Empty and a failure lists exactly which key/entry tripped it.
        static readonly Arrangement Facts = new();

        /// <summary>AC9 — every key has .Label and .Help</summary>
        [Fact]
        public void EveryKeyHasCopy() => Assert.Empty(Facts.KeysMissingCopy);

        /// <summary>AC10 — every resx {Key}.* names a key (both directions of F205.6)</summary>
        [Fact]
        public void NoOrphanCopy() => Assert.Empty(Facts.OrphanResxEntries);

        /// <summary>AC11 — every Number key has Min and Max</summary>
        [Fact]
        public void EveryNumberIsRanged() => Assert.Empty(Facts.UnrangedNumberKeys);

        /// <summary>AC12 — every static Choice value has a "Choice.{Key}.{value}" entry</summary>
        [Fact]
        public void EveryChoiceIsLabelled() => Assert.Empty(Facts.UnlabelledChoiceValues);

        /// <summary>The arrange step for every fact above: one pass over
        /// <see cref="StationSettingsAllowlist.All"/> and the shipped resx ResourceSet, producing
        /// four offender lists.</summary>
        sealed class Arrangement
        {
            public IReadOnlyList<string> KeysMissingCopy { get; }
            public IReadOnlyList<string> OrphanResxEntries { get; }
            public IReadOnlyList<string> UnrangedNumberKeys { get; }
            public IReadOnlyList<string> UnlabelledChoiceValues { get; }

            public Arrangement()
            {
                var resx = ShippedSettingsCopy.Entries();
                var keys = StationSettingsAllowlist.All;

                KeysMissingCopy = keys
                    .Where(setting => IsBlank(resx, $"{setting.Key}.Label") || IsBlank(resx, $"{setting.Key}.Help"))
                    .Select(setting => setting.Key)
                    .ToList();

                var expectedNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var setting in keys)
                {
                    expectedNames.Add($"{setting.Key}.Label");
                    expectedNames.Add($"{setting.Key}.Help");
                }
                // Same group-id rule as SettingCopy.GroupId (lowercase enum name); drift turns NoOrphanCopy red.
                foreach (var group in Enum.GetValues<SettingGroup>())
                    expectedNames.Add($"Group.{group.ToString().ToLowerInvariant()}.Label");
                foreach (var (key, value) in LabelledChoiceValues(keys))
                    expectedNames.Add($"Choice.{key}.{value}");
                // Key-agnostic (SPEC F205.7c, STORY-479, PLAN T580): SettingCopy.NotFoundLabel formats
                // ANY choice-kind key's saved-but-missing value through this one entry, never a
                // per-key "Choice.{key}.NotFound" name — so it never fits the {Key}.{value} shape the
                // loop just above enumerates.
                expectedNames.Add("Choice.NotFound");

                OrphanResxEntries = resx.Keys
                    .Where(name => !expectedNames.Contains(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                UnrangedNumberKeys = keys
                    .Where(setting => setting.Kind == SettingKind.Number && (setting.Min is null || setting.Max is null))
                    .Select(setting => setting.Key)
                    .ToList();

                UnlabelledChoiceValues = LabelledChoiceValues(keys)
                    .Where(pair => !resx.ContainsKey($"Choice.{pair.Key}.{pair.Value}"))
                    .Select(pair => $"{pair.Key}={pair.Value}")
                    .ToList();
            }

            /// <summary>Every (Key, value) pair a static-Choice, non-exempt setting's <see
            /// cref="AllowedSetting.Choices"/> carries — AC10's Choice.{Key}.{value} expected-name
            /// contribution and AC12's own subject set: the one rule both laws use.</summary>
            static IEnumerable<(string Key, string Value)> LabelledChoiceValues(IReadOnlyList<AllowedSetting> keys) =>
                keys
                    .Where(setting =>
                        setting.Kind == SettingKind.Choice
                        && setting.ChoiceSource == SettingChoiceSource.Static
                        && !ChoiceLabelExemptions.Contains(setting.Key))
                    .SelectMany(setting => (setting.Choices ?? []).Select(choice => (setting.Key, choice.Value)));

            static bool IsBlank(IReadOnlyDictionary<string, string> resx, string name) =>
                !resx.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>The AC12 exemption set's own guard, in its own Scenario so a renamed or removed
    /// allowlist key cannot leave a dead exemption behind unnoticed.</summary>
    public sealed class ScenarioChoiceExemptions
    {
        /// <summary>Exempt keys that are not a real, still-live reason to exempt: the allowlist must
        /// have the key with <c>Kind == SettingKind.Choice</c> AND <c>ChoiceSource ==
        /// SettingChoiceSource.Static</c>, or it's an offender. A key T579 moves off Static (e.g. to a
        /// catalog/store source) stops needing this exemption at all and would otherwise sit here dead.</summary>
        static readonly IReadOnlyList<string> NonChoiceExemptions = FeatureDescriptorlaw.ChoiceLabelExemptions
            .Where(key =>
                !StationSettingsAllowlist.ByKey.TryGetValue(key, out var setting)
                || setting.Kind != SettingKind.Choice
                || setting.ChoiceSource != SettingChoiceSource.Static)
            .ToList();

        [Fact]
        public void ExemptionsAreChoiceKeys() => Assert.Empty(NonChoiceExemptions);
    }
}
