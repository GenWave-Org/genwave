// STORY-401 — Uninstalling a pack refuses when something references it (SPEC F164.6, F165.6 · PLAN T413/T414)

namespace GenWave.Host.Tests.Specs;

public static class FeaturePackUninstallRefusesOnActiveReferences
{
    // ---------------------------------------------------------------------
    // SAD PATH — refusals first (the whole guard is a refusal contract)
    // ---------------------------------------------------------------------

    public sealed class ScenarioVoicePackRefusesOnActiveVoicePlan
    {
        [Fact]
        public void UninstallReturns409WhenAVoiceIdSitsInAnApprovedSpotsVoicePlan()
            => Assert.Fail("pending: T413 voice-pack DELETE guard — AC1");

        [Fact]
        public void The409ProblemDetailsListsTheReferencingSpotIds()
            => Assert.Fail("pending: T413 ProblemDetails body — AC1");

        [Fact]
        public void ThePackRowIsUntouchedOnRefusal()
            => Assert.Fail("pending: T413 WHERE NOT EXISTS ordering — AC1");
    }

    public sealed class ScenarioVoicePackRefusesOnPersonaReference
    {
        [Fact]
        public void UninstallReturns409WhenAPersonasVoiceReferencesAPackVoice()
            => Assert.Fail("pending: T413 persona-reference guard — AC2");

        [Fact]
        public void The409ProblemDetailsListsTheReferencingPersona()
            => Assert.Fail("pending: T413 ProblemDetails body — AC2");
    }

    public sealed class ScenarioJinglePackRefusesOnActiveBedMediaId
    {
        [Fact]
        public void UninstallReturns409WhenAPackMediaIdSitsInAReadyAdSpotsBedMediaId()
            => Assert.Fail("pending: T414 jingle-pack DELETE guard — AC4");

        [Fact]
        public void The409ProblemDetailsListsTheReferencingAdSpotIds()
            => Assert.Fail("pending: T414 ProblemDetails body — AC4");
    }

    public sealed class ScenarioTheGuardRunsInsideTheDelete
    {
        [Fact]
        public void AConcurrentInsertBetweenAnAdvisoryCheckAndDeleteStillLosesToTheInDeleteGuard()
            => Assert.Fail("pending: T413/T414 WHERE NOT EXISTS in the DELETE statement — AC6");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — uninstall succeeds when references are absent/inactive
    // ---------------------------------------------------------------------

    public sealed class ScenarioVoicePackUninstallSucceedsWhenReferencesAreRetiredOrFailed
    {
        [Fact]
        public void UninstallReturns204WhenReferencesAreOnlyInRetiredOrFailedSpots()
            => Assert.Fail("pending: T413 state-scoped guard — AC3");

        [Fact]
        public void ThePtFilesAreRemovedFromVoicesSlug()
            => Assert.Fail("pending: T413 file cleanup — AC3");

        [Fact]
        public void ThePackAndVoiceRowsAreGone()
            => Assert.Fail("pending: T413 cascade delete — AC3");
    }

    public sealed class ScenarioJinglePackUninstallSucceedsOtherwise
    {
        [Fact]
        public void UninstallReturns204WithNoActiveReferences()
            => Assert.Fail("pending: T414 happy path — AC5");

        [Fact]
        public void TheAuthoredJinglePacksSlugFolderIsGone()
            => Assert.Fail("pending: T414 file cleanup — AC5");

        [Fact]
        public void EveryAssociatedLibraryMediaRowIsDeleted()
            => Assert.Fail("pending: T414 media row delete — AC5");
    }
}
