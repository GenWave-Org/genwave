// STORY-395 — I install a voice pack and its voices go live without a restart (SPEC F164.1/.5, F166 · PLAN T413)
//
// Pending until PLAN T413 lands. Bodies use Assert.Fail so the /build-loop turns each Fact green as
// the surface builds; the Feature/Scenario/Specification shape is the contract until then.

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePackInstallGoesLiveWithoutARestart
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheVoicesVolumeIsSharedAndSeeded
    {
        [Fact]
        public void TheVoicesVolumeHoldsKokorosBakedIn54AfterFirstBoot()
            => Assert.Fail("pending: T412 (voice-seed init container) + T413 wire — AC1");

        [Fact]
        public void ASecondComposeUpIsANoOp()
            => Assert.Fail("pending: T412 idempotent copy — AC1");
    }

    public sealed class ScenarioApiWritesKokoroReads
    {
        [Fact]
        public void ApiCanWriteToTheSharedVolume()
            => Assert.Fail("pending: T412 mount posture — AC2");

        [Fact]
        public void KokoroCanReadFromTheSharedVolumeButNotWrite()
            => Assert.Fail("pending: T412 ro on kokoro — AC2");

        [Fact]
        public void KokoroReturnsANewVoiceIdInItsVoicesListing()
            => Assert.Fail("pending: T412 wire + T413 — AC2");
    }

    public sealed class ScenarioInstallWritesPtBytesToTheVolume
    {
        [Fact]
        public void EveryVoicesPtFileLandsAtTheExpectedPath()
            => Assert.Fail("pending: T413 install writes /voices/{slug}/{voiceId}.pt — AC3");

        [Fact]
        public void EveryPtFilesSha256MatchesTheManifestsPin()
            => Assert.Fail("pending: T413 hash verify — AC3");
    }

    public sealed class ScenarioInstallPersistsMetadata
    {
        [Fact]
        public void StationVoicePackHoldsExactlyOneRowKeyedBySlug()
            => Assert.Fail("pending: T413 upsert — AC4");

        [Fact]
        public void StationVoicePackVoiceHoldsOneRowPerManifestVoice()
            => Assert.Fail("pending: T413 upsert — AC4");
    }

    public sealed class ScenarioTheNewVoiceIsLiveWithoutARestart
    {
        [Fact]
        public void TheNextRenderRequestNamingThePackVoiceReceivesAudio()
            => Assert.Fail("pending: T413 + T421 wire — AC5 (kokoro rescan-per-request, no bounce)");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioABrokenPackRefusesCleanly
    {
        [Fact]
        public void ANoBytesAreWrittenWhenAPtSha256Mismatches()
            => Assert.Fail("pending: T413 hash-mismatch guard — AC6");

        [Fact]
        public void StationVoicePackIsUnchangedOnRefusal()
            => Assert.Fail("pending: T413 all-or-nothing install — AC6");

        [Fact]
        public void TheResponseIsA502IntegrityProblemDetails()
            => Assert.Fail("pending: T413 catalog transport integrity mapping — AC6");
    }
}
