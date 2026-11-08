// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T697)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Memory.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Bench.cs for the map.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsDefaults
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — the chosen model ships as default
    // entry point: WebApplicationFactory<Program> configuration (appsettings Llm:Model) + wizard default
    public sealed class ScenarioTheChosenModelShipsAsDefault
    {
        // Given Dean's pick
        // When  the defaults are updated

        [Fact(Skip = "pending: T697 — see docs/PLAN.md")]
        public void AppsettingsLlmModelAndTheWizardDefaultNameIt() => Assert.Fail("pending: T697 — appsettings Llm:Model and the wizard default name it");
    }
}
