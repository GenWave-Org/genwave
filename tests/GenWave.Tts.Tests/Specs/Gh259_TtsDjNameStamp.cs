// gh-#259 — a rendered segment carries its SPEAKER's persona as the item's DjName stamp
//
// Distinct from the F39 Artist CREDIT (Story131): Artist falls back to the station name; DjName is
// request.PersonaName verbatim — a station-voiced segment has no DJ of its own (the Orchestrator
// stamps a StationId's show attribution itself), so no fallback ever fabricates one here.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTtsDjNameStamp
{
    static TtsSegmentSource BuildSource(
        FakeTtsSynthesizer synth,
        FakeLoudnessAnalyzer analyzer,
        string cacheRoot) =>
        new(
            new FakeSegmentCopyWriter("Some patter copy"),
            synth,
            analyzer,
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            NullLogger<TtsSegmentSource>.Instance);

    static SegmentRequest Request(SegmentKind kind, string? personaName, MediaItem? track = null) =>
        new(kind, "af_heart", "GenWave", track, DateTimeOffset.UtcNow, "test-station", personaName);

    static MediaItem MakeTrack(string id) =>
        new(id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-16.0, -1.0, true));

    public sealed class ScenarioSpeakerAttribution : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task APersonaVoicedSegmentCarriesThePersonaAsDjName()
        {
            var source = BuildSource(synth, analyzer, cacheRoot);
            var item = await source.RenderAsync(
                Request(SegmentKind.LeadIn, "DJ Nova", MakeTrack("next")), CancellationToken.None);
            Assert.Equal("DJ Nova", item!.DjName);
        }

        [Fact]
        public async Task AStationVoicedSegmentCarriesNullDjNameButKeepsTheStationCredit()
        {
            // PersonaName null (gh-#96's StationId shape): Artist still credits the station —
            // DjName must NOT inherit that fallback, or "no DJ on shift" would display as a DJ
            // named after the station.
            var source = BuildSource(synth, analyzer, cacheRoot);
            var item = await source.RenderAsync(
                Request(SegmentKind.StationId, personaName: null), CancellationToken.None);
            Assert.Equal("GenWave", item!.Artist);
            Assert.Null(item.DjName);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
