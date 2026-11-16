// STORY-536 AC2 (render half) — the renderer carries the handoff pair onto the item (SPEC F218.15(b) · PLAN T726)
//
// MediaItem.Handoff is the same instance the planner stamped on the request, on a fresh render and on a
// cache hit alike, the way CrosstalkScript rides through.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureAHandoffSurvivesTheRender
{
    static TtsSegmentSource BuildSource(FakeTtsSynthesizer synth, string cacheRoot) =>
        new(
            new FakeSegmentCopyWriter("Some patter copy", freshPerAiring: true), // a SignOff only airs LLM-fresh copy (F144.4 guard)
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            NullLogger<TtsSegmentSource>.Instance);

    static SegmentRequest SignOff(HandoffParticipants handoff) =>
        new(SegmentKind.SignOff, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station", "DJ Alpha")
        {
            Handoff = handoff,
        };

    public sealed class ScenarioASignOffRender : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly HandoffParticipants handoff = new(
            new CrosstalkParticipant(10, "DJ Alpha"), new CrosstalkParticipant(20, "DJ Beta"), DateTimeOffset.UnixEpoch);

        [Fact]
        public async Task AFreshRenderCarriesTheHandoff()
        {
            var item = await BuildSource(synth, cacheRoot).RenderAsync(SignOff(handoff), CancellationToken.None);

            Assert.Same(handoff, item?.Handoff);
        }

        [Fact]
        public async Task ACacheHitCarriesTheHandoff()
        {
            var source = BuildSource(synth, cacheRoot);
            await source.RenderAsync(SignOff(handoff), CancellationToken.None);
            var callsAfterFirst = synth.CallCount;

            var item = await source.RenderAsync(SignOff(handoff), CancellationToken.None);

            Assert.Equal(callsAfterFirst, synth.CallCount);
            Assert.Same(handoff, item?.Handoff);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
