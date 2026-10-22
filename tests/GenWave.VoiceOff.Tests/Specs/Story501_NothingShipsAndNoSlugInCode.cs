// STORY-501 — Nothing ships, and no slug in code (gh-#614 · SPEC F214.16, F214.17 · PLAN T606)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.
// AC1, AC2, AC6 (the laws) live in GenWave.Architecture.Tests' Story501_VoiceOffSlugFence.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureNothingShipsAndNoSlugInCode
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the tool references only Tts and Loudness
    public sealed class ScenarioTheToolReferencesOnlyTtsAndLoudness
    {
        // Given tools/VoiceOff/VoiceOff.csproj
        // When  its ProjectReferences are listed

        [Fact(Skip = "pending: T606 — see docs/PLAN.md")]
        public void TheyAreGenWaveTtsAndGenWaveLoudness() => Assert.Fail("pending: T606 — they are GenWave.Tts and GenWave.Loudness");
    }

    // AC4 — the solution builds it
    public sealed class ScenarioTheSolutionBuildsIt
    {
        // Given GenWave.sln
        // When  `dotnet build GenWave.sln` runs

        [Fact(Skip = "pending: T606 — see docs/PLAN.md")]
        public void VoiceOffDllIsProduced() => Assert.Fail("pending: T606 — VoiceOff.dll is produced");
    }

    // AC5 — help lists every verb
    public sealed class ScenarioHelpListsEveryVerb
    {
        // Given the built tool
        // When  `voiceoff --help` runs

        [Fact(Skip = "pending: T606 — see docs/PLAN.md")]
        public void RenderPairServeReelAndScoreEachPrintWithAnExample() => Assert.Fail("pending: T606 — render, pair, serve, reel and score each print with an example");
    }
}
