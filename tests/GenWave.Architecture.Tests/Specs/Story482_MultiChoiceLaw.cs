// STORY-482 — MultiChoice fitness law (gh-#778 · SPEC F205.7h · PLAN T585)
//
// BDD specification — xUnit. Reads what SHIPS, not the source file — the same
// StationSettingsAllowlist.All / shipped-resx-ResourceSet posture Story477_DescriptorLaw's own file
// header documents, kept as its own STORY-482 law file (rather than a third scenario bolted onto
// Story477's) since it pins a rule this story, not STORY-477, introduces.

using GenWave.Architecture.Tests.Support;
using GenWave.Host.Configuration;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureMultiChoiceLaw
{
    public sealed class ScenarioEveryMultiChoiceHasASource
    {
        // Given: StationSettingsAllowlist.All's own MultiChoice entries

        static readonly IReadOnlyList<string> Offenders = StationSettingsAllowlist.All
            .Where(setting =>
                setting.Kind == SettingKind.MultiChoice
                && setting.ChoiceSource == SettingChoiceSource.Static
                && (setting.Choices is null || setting.Choices.Count == 0))
            .Select(setting => setting.Key)
            .ToList();

        /// <summary>SPEC F205.7h — every MultiChoice entry names a non-Static ChoiceSource (a live
        /// Probe/Catalog) or carries its own non-empty frozen Choices list: a checkbox control with
        /// nothing to ever check is a descriptor bug, not a runtime one — this fails at build/test
        /// time rather than surfacing as an empty admin UI section.</summary>
        [Fact]
        public void EveryMultiChoiceHasASource() => Assert.Empty(Offenders);
    }

    public sealed class ScenarioNotFoundCopyShips
    {
        // Given: the shipped resx ResourceSet

        static readonly IReadOnlyDictionary<string, string> Resx = ShippedSettingsCopy.Entries();

        /// <summary>SPEC F205.7h — <c>Choice.NotFound</c> ships. Already added at T580 for
        /// <see cref="SettingKind.Choice"/>'s own single-value append; <see cref="SettingKind.MultiChoice"/>'s
        /// per-slug append (<c>SettingChoiceResolver.AppendMissing</c>) reads the SAME
        /// key-agnostic entry, never a second one — this fact confirms it ships, it does not
        /// duplicate Story477_DescriptorLaw's own expected-name bookkeeping for it.</summary>
        [Fact]
        public void NotFoundCopyShips() => Assert.True(Resx.ContainsKey("Choice.NotFound"));
    }
}
