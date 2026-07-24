// STORY-223 — Players see the cover of what's on air (SPEC F88.4–F88.5, PLAN T85)
//
// BDD specification — xUnit, authored PENDING at /plan time. Feeder annotation facts drive
// the production annotation builder; the engine-side icy_metadata line is a static guard on
// genwave.liq (zero-diff epoch deliberately broken here, re-pinned at T93). The live ICY
// observation (F88.5) is T85's compose-stack acceptance, not a unit fact.

using GenWave.Core.Domain;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtworkEmission
{
    const string PublicBaseUrl = "https://example.test";

    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    static ArtworkUrlResolver Resolver(string publicBaseUrl) => new(
        new FakeOptionsMonitor<StationOptions>(new StationOptions { PublicBaseUrl = publicBaseUrl }),
        new FakeArtworkTokenStore());

    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string EngineScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    public static class ScenarioAnnotationsCarryArtworkUrls
    {
        [Fact]
        public static async Task AMusicPushCarriesItsTokenArtworkUrl()
        {
            // Given Station:PublicBaseUrl set  When the feeder annotates a music request
            // Then url=<base>/spectator/api/artwork/<token> rides the annotation.
            var item = new MediaItem("42", "/media/42.mp3", "Title", DefaultLoudness);
            var artworkUrl = await Resolver(PublicBaseUrl).ResolveAsync(item, CancellationToken.None);

            var annotation = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave", artworkUrl);

            Assert.Contains(
                $"url=\"{PublicBaseUrl}/spectator/api/artwork/tok42\",", annotation, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task ATtsPushCarriesTheStationIconUrl()
        {
            // Given Station:PublicBaseUrl set  When the feeder annotates a tts: push
            // Then url=<base>/spectator/api/artwork/station rides the annotation — the reserved
            // path segment F88.3's own no-oracle fallback (a malformed, non-32-hex token) already
            // resolves to the station icon, so no dedicated route is needed.
            var item = new MediaItem("tts:abc123", "/tts/abc123.wav", "GenWave", DefaultLoudness);
            var artworkUrl = await Resolver(PublicBaseUrl).ResolveAsync(item, CancellationToken.None);

            var annotation = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave", artworkUrl);

            Assert.Contains(
                $"url=\"{PublicBaseUrl}/spectator/api/artwork/station\",", annotation, StringComparison.Ordinal);
        }

        [Fact]
        public static void TheEngineScriptForwardsUrlInIcyMetadata()
        {
            // genwave.liq's output.icecast icy_metadata list includes "url" — static guard.
            Assert.Matches(@"icy_metadata\s*=\s*\[[^\]]*""url""[^\]]*\]", EngineScriptText);
        }
    }

    public static class SadPathUnsetBase
    {
        [Fact]
        public static async Task AnEmptyPublicBaseUrlEmitsNoUrlAnnotationAnywhere()
        {
            // The default deployment stays byte-identical to pre-F88 annotations.
            var musicItem = new MediaItem("42", "/media/42.mp3", "Title", DefaultLoudness);
            var ttsItem = new MediaItem("tts:abc123", "/tts/abc123.wav", "GenWave", DefaultLoudness);
            var resolver = Resolver(string.Empty);

            var musicArtworkUrl = await resolver.ResolveAsync(musicItem, CancellationToken.None);
            var ttsArtworkUrl = await resolver.ResolveAsync(ttsItem, CancellationToken.None);

            var musicAnnotation = LiquidsoapAnnotationBuilder.Build(musicItem, 0.0, "st-01", "GenWave", musicArtworkUrl);
            var ttsAnnotation = LiquidsoapAnnotationBuilder.Build(ttsItem, 0.0, "st-01", "GenWave", ttsArtworkUrl);

            Assert.Equal(LiquidsoapAnnotationBuilder.Build(musicItem, 0.0, "st-01", "GenWave"), musicAnnotation);
            Assert.Equal(LiquidsoapAnnotationBuilder.Build(ttsItem, 0.0, "st-01", "GenWave"), ttsAnnotation);
            Assert.DoesNotContain("url=", musicAnnotation, StringComparison.Ordinal);
            Assert.DoesNotContain("url=", ttsAnnotation, StringComparison.Ordinal);
        }
    }
}
