// STORY-131 — Patter airs credited to the active DJ, on every surface (Epic U / SPEC F39,
// closes gitea-#212) — artist-stamping half. The request-shape half lives in
// Orchestration.Tests/Specs/Story131_PersonaAttributionRequestShape.cs. The gw_icy_song ICY
// collapse (F39.5, U5) is an engine-script function with no unit seam — its repo-content pin and
// live ICY spot-check live in Story133 (the gate).
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-13, house rule since Epic S).
// Implemented U4 (2026-07-13).

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePersonaAttributionArtistStamping
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    static TtsSegmentSource BuildSource(
        FakeTtsSynthesizer synth,
        FakeLoudnessAnalyzer analyzer,
        FakeCueAnalyzer cueAnalyzer,
        string cacheRoot,
        string copyText = "Some patter copy") =>
        new(
            new FakeSegmentCopyWriter(copyText),
            synth,
            analyzer,
            cueAnalyzer,
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            NullLogger<TtsSegmentSource>.Instance);

    static SegmentRequest Request(SegmentKind kind, string? personaName, MediaItem? track = null) =>
        new(kind, "af_heart", "GenWave", track, DateTimeOffset.UtcNow, "test-station", personaName);

    static MediaItem MakeTrack(string id) =>
        new(id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-16.0, -1.0, true));

    // ---------------------------------------------------------------------
    // HAPPY PATH — every kind credits the active persona
    // ---------------------------------------------------------------------

    public sealed class ScenarioPatterCarriesThePersonaName : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task AStationIdRendersWithThePersonaAsArtist()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(Request(SegmentKind.StationId, "DJ Nova"), CancellationToken.None);
            Assert.Equal("DJ Nova", item!.Artist);
        }

        [Fact]
        public async Task ATimeDateRendersWithThePersonaAsArtist()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(Request(SegmentKind.TimeDate, "DJ Nova"), CancellationToken.None);
            Assert.Equal("DJ Nova", item!.Artist);
        }

        [Fact]
        public async Task ALeadInRendersWithThePersonaAsArtist()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(
                Request(SegmentKind.LeadIn, "DJ Nova", MakeTrack("next")), CancellationToken.None);
            Assert.Equal("DJ Nova", item!.Artist);
        }

        [Fact]
        public async Task ABackAnnounceRendersWithThePersonaAsArtist()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(
                Request(SegmentKind.BackAnnounce, "DJ Nova", MakeTrack("prev")), CancellationToken.None);
            Assert.Equal("DJ Nova", item!.Artist);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — attribution is per-airing, not baked into cached content
    // ---------------------------------------------------------------------

    public sealed class ScenarioAttributionIsPerAiringNotCachedContent : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeLoudnessAnalyzer analyzer = new();

        // FakeTtsSynthesizer writes to {OutputDirectory}/{hash}.wav using the same hash formula as
        // KokoroTtsSynthesizer, so setting OutputDirectory = cacheRoot means the second render finds
        // the file on disk and skips synthesis (mirrors Story005's ScenarioCacheHitAvoidsResynthesis).
        FakeTtsSynthesizer BuildSynthForCache() => new() { OutputDirectory = cacheRoot };

        [Fact]
        public async Task ACacheHitRenderCarriesTheNewlyActivatedPersonasName()
        {
            // F39.3 — same (text,voice,station), persona-less then persona-activated: the SECOND
            // render's MediaItem carries the newly-active persona's name even though the underlying
            // audio file is the untouched cache hit.
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);

            await source.RenderAsync(Request(SegmentKind.StationId, personaName: null), CancellationToken.None);
            var second = await source.RenderAsync(
                Request(SegmentKind.StationId, personaName: "DJ Nova"), CancellationToken.None);

            Assert.Equal("DJ Nova", second!.Artist);
        }

        [Fact]
        public async Task TheCacheFileIsReusedAcrossAPersonaSwitch()
        {
            // F39.3 — the (text,voice,station) cache key excludes PersonaName, so the switch above
            // must NOT trigger a resynthesis: the synthesizer is called exactly once for both renders.
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);

            await source.RenderAsync(Request(SegmentKind.StationId, personaName: null), CancellationToken.None);
            await source.RenderAsync(Request(SegmentKind.StationId, personaName: "DJ Nova"), CancellationToken.None);

            Assert.Equal(1, synth.CallCount);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioPersonaLessAndSafeContentStayStationBranded : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task PersonaLessPatterKeepsTheStationNameAsArtist()
        {
            // F39.2 — no active persona -> Artist = StationName, byte-identical to today.
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(Request(SegmentKind.StationId, personaName: null), CancellationToken.None);
            Assert.Equal("GenWave", item!.Artist);
        }

        [Fact]
        public async Task AnActivePersonaNeverChangesAnAuthoredSafeSegmentsBranding()
        {
            // F39.4 — SafeSegmentAuthor's own request type (SafeSegmentRequest) has no PersonaName
            // field at all: there is structurally no channel for an active persona to reach it, so
            // AuthorAsync always tags the authored row with request.StationName (the gitea-#172 rule),
            // regardless of any persona active elsewhere in the station. Exercising the REAL
            // authoring pipeline (fakes only at its I/O seams) is the strongest honest assertion
            // available at this unit seam.
            var authorRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var authorSynth = new FakeTtsSynthesizer();
            var mixer = new FakeAudioMixer();
            var loudness = new FakeLoudnessAnalyzer();
            var cue = new FakeCueAnalyzer();
            var energy = new FakeEnergyAnalyzer();
            var writer = new FakeAuthoredCatalogWriter();
            var author = new SafeSegmentAuthor(
                authorSynth, mixer, loudness, cue, energy, writer,
                Options.Create(new TtsOptions { Format = "wav" }), NullLogger<SafeSegmentAuthor>.Instance);
            var request = new SafeSegmentRequest(
                "Please stand by.", LibraryId: 1, StationName: "GenWave", DefaultVoice: "af_heart",
                AuthoredRoot: authorRoot, BedDuckDb: -12.0, BedPadSeconds: 1.5);

            try
            {
                await author.AuthorAsync(request, CancellationToken.None);
                Assert.Equal("GenWave", writer.LastInsert!.Tags.Artist);
            }
            finally
            {
                if (Directory.Exists(authorRoot)) Directory.Delete(authorRoot, recursive: true);
                if (Directory.Exists(authorSynth.OutputDirectory)) Directory.Delete(authorSynth.OutputDirectory, recursive: true);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
