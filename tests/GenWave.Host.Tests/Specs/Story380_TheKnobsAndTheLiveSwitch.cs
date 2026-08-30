// STORY-380 — The knobs and the laws (SPEC F155 · PLAN T357, T366)
//
// BDD specification — xUnit. PENDING until T357/T366. Entry-point discipline: every fact drives
// the REAL production binary (WebApplicationFactory<Program>, the Story345/Story366 factory
// idiom over an ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/
// EphemeralStationDatabase), reading GardenerOptions off the booted host's DI container and
// driving Station:Thumbs:Enabled through the real PUT /api/settings surface. AC3 (the L5
// reserved-namespace pin) and AC4 (the three-way disjointness pin) live in
// GenWave.Architecture.Tests, written by another agent — not this file.
namespace GenWave.Host.Tests.Specs;

public static class FeatureTheKnobsAndTheLiveSwitch
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — sane defaults, and the one Live knob flips without a restart
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaults
    {
        // Given no Gardener__* env, When the api boots.
        [Fact(Skip = "pending T357 (STORY-380 AC1)")]
        public void TheBoundOptionsMatchTheDocumentedDefaults() => Assert.Fail("pending T357");
    }

    public sealed class ScenarioTheLiveSwitch
    {
        // Given Station:Thumbs:Enabled false, When PUT /api/settings sets it true.
        [Fact(Skip = "pending T366 (STORY-380 AC2)")]
        public void TheNextThumbPostIsAcceptedWithNoRestart() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioDisclosure
    {
        // Given thumbs enabled, When the F67 disclosure suites run.
        [Fact(Skip = "pending T366 (STORY-380 AC5)")]
        public void TheNowPlayingContractIsThePinnedSetPlusAiring() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-380 AC5)")]
        public void TheThumbsTwoOhTwoBodyIsThePinnedConstant() => Assert.Fail("pending T366");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a bad knob never reaches a running station
    // ---------------------------------------------------------------------

    public sealed class ScenarioABadKnobFailsBoot
    {
        // Given Gardener__NudgeGain=7, When the api boots.
        [Fact(Skip = "pending T357 (STORY-380 AC6)")]
        public void ItExitsNamingTheOffendingKey() => Assert.Fail("pending T357");

        [Fact(Skip = "pending T357 (STORY-380 AC6)")]
        public void ItExitsNamingTheAllowedRange() => Assert.Fail("pending T357");
    }
}
