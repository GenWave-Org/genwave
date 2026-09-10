// STORY-424 — AdPreviewRoot.IsUnder's own canonical-root check (SPEC F174.4 · PLAN T442) — the ONE
// construction/check site AdRenderService (writer), AdsController.PreviewWav
// (reader, GenWave.Host), and AdSpotLifecycleGuardianService (deleter) all share. Mutation 7 (the
// AdPreviewRoot.IsUnder call removed from PreviewWav) is proven dead by the Host-side
// PreviewWavIs404WhenTheStoredPathEscapesThePreviewRoot fact — this file pins the helper's OWN
// correctness directly, no HTTP involved.

namespace GenWave.Ads.Tests.Specs;

public static class FeaturePreviewRootIsUnder
{
    const string Root = "/authored/preview";

    public sealed class ScenarioIsUnder
    {
        [Fact]
        public void APathInsideTheRootIsUnderIt()
            => Assert.True(AdPreviewRoot.IsUnder(Root, "/authored/preview/7-abc123.wav"));

        [Fact]
        public void TheRootItselfIsUnderIt()
            => Assert.True(AdPreviewRoot.IsUnder(Root, Root));

        [Fact]
        public void ANestedSubdirectoryIsUnderIt()
            => Assert.True(AdPreviewRoot.IsUnder(Root, "/authored/preview/nested/7-abc123.wav"));

        [Fact]
        public void APathOutsideTheRootIsNotUnderIt()
            => Assert.False(AdPreviewRoot.IsUnder(Root, "/authored/ads/7.wav"));

        /// <summary>The exact drift a bare <c>StartsWith(canonicalRoot)</c> (no separator) would miss:
        /// <c>/authored/previewX</c> shares <c>/authored/preview</c> as a STRING PREFIX but is a
        /// completely different, sibling directory — never "under" it.</summary>
        [Fact]
        public void ASiblingDirectorySharingTheRootAsAStringPrefixIsNotUnderIt()
            => Assert.False(AdPreviewRoot.IsUnder(Root, "/authored/previewX/a.wav"));

        /// <summary>Every real call site resolves its candidate through <c>Path.GetFullPath</c> BEFORE
        /// calling <see cref="AdPreviewRoot.IsUnder"/> (a raw, unresolved <c>..</c> segment reaching
        /// this method's own string comparison would defeat the whole guard, since <c>StartsWith</c>
        /// alone cannot see through it) — this fact drives that exact resolve-then-check sequence: a
        /// <c>..</c> segment that walks the candidate OUT of the preview root resolves to a path this
        /// method correctly reports as not under it.</summary>
        [Fact]
        public void AResolvedTraversalThatEscapesTheRootIsNotUnderIt()
        {
            var escaped = Path.GetFullPath(Path.Combine(Root, "..", "secret", "7.wav"));
            Assert.False(AdPreviewRoot.IsUnder(Root, escaped));
        }
    }
}
