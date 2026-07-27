// STORY-242 — Upgrading changes nothing on the air: the key leaves the surface
// (SPEC F91.5/F91.6 AC3, PLAN T120)
//
// BDD specification — xUnit, pending. Entry-point discipline: drives the real
// PUT /api/settings through WebApplicationFactory<Program>. Companion to the
// migration facts in Story242_UpgradeChangesNothing.cs (MediaLibrary.Tests).

namespace GenWave.Host.Tests.Specs;

public static class FeatureActiveIdKeyRetired
{
    public sealed class ScenarioWritingTheRetiredKey
    {
        // Given the migrated station, When PUT /api/settings writes Station:Persona:ActiveId.

        [Fact(Skip = "Pending (T120)")]
        public void WriteIsRejectedAsUnknownKey() { }

        [Fact(Skip = "Pending (T120)")]
        public void SettingsListingNoLongerContainsTheKey() { }
    }
}
