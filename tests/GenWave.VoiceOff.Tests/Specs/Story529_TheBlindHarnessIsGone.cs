// STORY-529 — The blind harness is gone and nothing ships (gh-#614 · SPEC F217 · PLAN T660)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheBlindHarnessIsGone
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — help lists three verbs
    public sealed class ScenarioHelpListsThreeVerbs
    {
        // Given the built tool
        // When  voiceoff --help runs

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void ItListsRenderServeAndScore() => Assert.Fail("pending: T660 — it lists render, serve and score");

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void ItListsNothingElse() => Assert.Fail("pending: T660 — it lists nothing else");
    }

    // AC2 — the old folders are gone
    public sealed class ScenarioTheOldFoldersAreGone
    {
        // Given tools/VoiceOff
        // When  its folders are listed

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void PairingReelingAndTimingDoNotExist() => Assert.Fail("pending: T660 — Pairing, Reeling and Timing do not exist");

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void GauntletDoesNotExist() => Assert.Fail("pending: T660 — gauntlet/ does not exist");
    }

    // AC3 — the old specs are gone
    public sealed class ScenarioTheOldSpecsAreGone
    {
        // Given tests/GenWave.VoiceOff.Tests/Specs
        // When  its files are listed

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void Story496ToStory500DoNotExist() => Assert.Fail("pending: T660 — Story496 to Story500 do not exist");
    }

    // AC4 — the slug fence allows the data folders
    public sealed class ScenarioTheSlugFenceAllowsTheDataFolders
    {
        // Given the L12 law
        // When  a slug is planted under cards/ and under script/

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void TheLawPasses() => Assert.Fail("pending: T660 — the law passes");
    }

    // AC5 — the tool still references only Tts and Loudness
    public sealed class ScenarioTheToolStillReferencesOnlyTtsAndLoudness
    {
        // Given VoiceOff.csproj
        // When  its ProjectReferences are listed

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void TheyAreGenWaveLoudnessAndGenWaveTts() => Assert.Fail("pending: T660 — they are GenWave.Loudness and GenWave.Tts");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — a retired verb is unknown
    public sealed class ScenarioARetiredVerbIsUnknown
    {
        // Given the built tool
        // When  voiceoff pair runs

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void ItExits2() => Assert.Fail("pending: T660 — it exits 2");
    }

    // AC7 — a slug in code still fails the fence
    public sealed class ScenarioASlugInCodeStillFailsTheFence
    {
        // Given the L12 law
        // When  a slug is planted under Render/

        [Fact(Skip = "pending: T660 — see docs/PLAN.md")]
        public void TheLawFails() => Assert.Fail("pending: T660 — the law fails");
    }
}
