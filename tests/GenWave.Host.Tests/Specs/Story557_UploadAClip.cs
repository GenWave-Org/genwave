// STORY-557 — Upload a clip, get a voice (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureUploadAClip
{
    // AC1–AC3 — a 10 s WAV and a label posted to api/voices/clips
    public sealed class ScenarioAGoodUpload
    {
        [Fact(Skip = "pending: T756")]
        public void ARowExists() { }

        [Fact(Skip = "pending: T756")]
        public void TheStoredFileIsMono() { }

        [Fact(Skip = "pending: T756")]
        public void TheStoredFileIs24Kilohertz() { }

        [Fact(Skip = "pending: T756")]
        public void TheStoredFileIsAtMinus20Lufs() { }

        [Fact(Skip = "pending: T756")]
        public void TheClipIsListed() { }
    }

    // AC4
    public sealed class ScenarioDeletingAFreeClip
    {
        [Fact(Skip = "pending: T756")]
        public void TheRowIsGone() { }

        [Fact(Skip = "pending: T756")]
        public void TheFileIsGone() { }
    }

    // AC5, AC11 — a fresh install
    public sealed class ScenarioTheStarterSet
    {
        [Fact(Skip = "pending: T757")]
        public void StarterClipsExist() { }

        [Fact(Skip = "pending: T757")]
        public void EveryStarterClipHasACredit() { }

        [Fact(Skip = "pending: T757")]
        public void NoCreditNamesANonCommercialLicence() { }
    }

    // Sad path
    // AC7, AC9, AC10
    public sealed class ScenarioRefusedUploads
    {
        [Fact(Skip = "pending: T756")]
        public void AThreeSecondClipReturns400WithTheReason() { }

        [Fact(Skip = "pending: T756")]
        public void AnOversizeFileReturns400() { }

        [Fact(Skip = "pending: T756")]
        public void AnAnonymousUploadReturns401() { }

        [Fact(Skip = "pending: T756")]
        public void AnAnonymousDeleteReturns401() { }

        [Fact(Skip = "pending: T756")]
        public void AHostileLabelIsStoredUnderASlugInsideTheClipsFolder() { }
    }

    // Sad path
    // AC8 — a DJ's voice map names the clip
    public sealed class ScenarioDeletingAClipInUse
    {
        [Fact(Skip = "pending: T756")]
        public void TheAnswerIs409() { }

        [Fact(Skip = "pending: T756")]
        public void TheAnswerNamesTheDj() { }
    }
}
