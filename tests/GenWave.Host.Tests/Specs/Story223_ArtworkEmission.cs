// STORY-223 — Players see the cover of what's on air (SPEC F88.4–F88.5, PLAN T85)
//
// BDD specification — xUnit, authored PENDING at /plan time. Feeder annotation facts drive
// the production annotation builder; the engine-side icy_metadata line is a static guard on
// genwave.liq (zero-diff epoch deliberately broken here, re-pinned at T93). The live ICY
// observation (F88.5) is T85's compose-stack acceptance, not a unit fact.

using GenWave.Core.Domain;
using GenWave.Host.Artwork;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtworkEmission
{
    const string PublicBaseUrl = "https://example.test";

    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    static ArtworkUrlResolver Resolver(string publicBaseUrl) => new(
        new FakeOptionsMonitor<StationOptions>(new StationOptions { PublicBaseUrl = publicBaseUrl }),
        new FakeArtworkTokenStore(), new FakeActivePersonaAccessor(),
        new PersonaAvatarTokenCache(
            new FakePersonaAvatarStore(), TimeProvider.System, NullLogger<PersonaAvatarTokenCache>.Instance));

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

    /// <summary>
    /// SPEC F88.5 fail-closed extension (PLAN T125 review F2) — <see cref="ArtworkUrlResolver.IsTrusted"/>,
    /// the OTHER direction of this same type: whether a url the ENGINE echoed back on an
    /// engine-initiated advance's output metadata is one THIS station actually stamped, versus a
    /// hostile file tag's own <c>url</c>-shaped field.
    /// </summary>
    public static class ScenarioIsTrustedGatesTheEngineEcho
    {
        [Fact]
        public static async Task ALegitimateTokenUrlPasses()
        {
            var item = new MediaItem("42", "/media/42.mp3", "Title", DefaultLoudness);
            var resolver = Resolver(PublicBaseUrl);
            var echoedUrl = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.True(resolver.IsTrusted(echoedUrl!));
        }

        [Fact]
        public static void AHostileFileTagUrlIsRejected()
        {
            // A safe-rotation play's own Vorbis URL=/ID3 W-frame tag, echoed back by genwave.liq
            // indistinguishably from our own stamped annotation at the output-metadata layer.
            var resolver = Resolver(PublicBaseUrl);

            Assert.False(resolver.IsTrusted("https://evil.example/tracking-pixel.gif"));
        }

        [Fact]
        public static void AnEmptyPublicBaseUrlTrustsNothing()
        {
            // F88.5 extended to the echo side: an empty base can never be a legitimate prefix of
            // anything, so even a url that HAPPENS to look like our own shape is never trusted.
            var resolver = Resolver(string.Empty);

            Assert.False(resolver.IsTrusted($"{PublicBaseUrl}/spectator/api/artwork/tok42"));
        }
    }
}
