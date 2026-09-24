namespace GenWave.Host.Tests.Support.Localization;

/// <summary>
/// Test-only marker type that pins the resx-file-name-matches-marker-type-name convention
/// <see cref="GenWave.Host.Configuration.SettingsResources"/> itself relies on (PLAN T573) — proven
/// here in isolation, against a throwaway <c>Greeting</c> key and a real
/// <c>TestSettingsResources.fr.resx</c> satellite, instead of against the Host's own (untranslated)
/// resx. Never shipped in the Host assembly; lives only in this test project. See
/// <c>Story477SettingsResxNamingConvention</c> for the spec this backs.
/// </summary>
public sealed class TestSettingsResources
{
}
