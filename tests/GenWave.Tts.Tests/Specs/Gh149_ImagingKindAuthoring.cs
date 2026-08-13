// gh-#149 — Authored segments carry a Station Imaging content kind (authoring-pipeline half).
//
// BDD specification — xUnit. SafeSegmentAuthor threads SafeSegmentRequest.Kind through to the
// AuthoredMediaInsert unchanged, and an unspecified kind is the Liner default (today's behavior).
// Kinds are METADATA-ONLY: nothing else about the render/measure/insert pipeline varies by kind —
// the storage/read halves live in MediaLibrary.Tests (Gh149_ImagingKindAuthoredRows) and the wire
// half in Host.Tests (Gh149_ImagingKindEndpoint). Fakes at every seam, mirroring Story078.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureImagingKindAuthoring
{
    // ------------------------------------------------------------------
    // Shared fixture helpers (the Story078 shape)
    // ------------------------------------------------------------------

    static SafeSegmentAuthor BuildAuthor(
        FakeTtsSynthesizer synth,
        FakeAudioMixer mixer,
        FakeLoudnessAnalyzer loudness,
        FakeCueAnalyzer cue,
        FakeEnergyAnalyzer energy,
        FakeAuthoredCatalogWriter writer)
    {
        var opts = Options.Create(new TtsOptions { Format = "wav" });
        return new SafeSegmentAuthor(
            synth, NoCorrections.PronunciationProvider(), mixer, loudness, cue, energy, writer, opts,
            NullLogger<SafeSegmentAuthor>.Instance);
    }

    static SafeSegmentRequest Request(string authoredRoot, ImagingKind? kind = null) =>
        new(
            Text: "Please stand by.",
            LibraryId: 1,
            StationName: "GenWave",
            DefaultVoice: "af_heart",
            AuthoredRoot: authoredRoot,
            BedDuckDb: -12.0,
            BedPadSeconds: 1.5,
            Kind: kind ?? ImagingKind.Liner);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the requested kind reaches the insert seam
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheRequestedKindReachesTheInsert : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task AJingleRequestInsertsAJingleRow()
        {
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            var result = await author.AuthorAsync(Request(authoredRoot, ImagingKind.Jingle), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(ImagingKind.Jingle, writer.LastInsert!.Kind);
        }

        [Fact]
        public async Task AStationIdRequestInsertsAStationIdRow()
        {
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot, ImagingKind.StationId), CancellationToken.None);

            Assert.Equal(ImagingKind.StationId, writer.LastInsert!.Kind);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — an unspecified kind defaults to Liner (today's behavior)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnspecifiedKindDefaultsToLiner : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task ARequestBuiltWithoutAKindInsertsALinerRow()
        {
            // The boot seed and every pre-#149 caller construct SafeSegmentRequest without
            // naming Kind — the record default must land Liner at the insert seam.
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            var request = new SafeSegmentRequest(
                Text: "Please stand by.",
                LibraryId: 1,
                StationName: "GenWave",
                DefaultVoice: "af_heart",
                AuthoredRoot: authoredRoot,
                BedDuckDb: -12.0,
                BedPadSeconds: 1.5);

            await author.AuthorAsync(request, CancellationToken.None);

            Assert.Equal(ImagingKind.Liner, writer.LastInsert!.Kind);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
