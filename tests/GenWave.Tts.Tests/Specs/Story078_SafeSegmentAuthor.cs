// STORY-078 — SafeSegmentAuthor renders a segment end-to-end
//
// BDD specification — xUnit. SPEC F27.1–F27.5. SafeSegmentAuthor (in GenWave.Tts) composes
// shipped ITtsSynthesizer + P2's IAudioMixer + shipped ILoudnessAnalyzer/ICueAnalyzer/IEnergyAnalyzer
// + P4's IAuthoredCatalogWriter, all-or-nothing. Fakes at every seam — no Postgres, no Kokoro, no
// ffmpeg in this project's specs (those live in MediaLibrary.Tests' / Loudness's own specs).

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureSafeSegmentAuthor
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
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
            synth, mixer, loudness, cue, energy, writer, opts, NullLogger<SafeSegmentAuthor>.Instance);
    }

    static SafeSegmentRequest Request(
        string authoredRoot,
        string text = "Please stand by.",
        long libraryId = 1,
        string stationName = "GenWave",
        string defaultVoice = "af_heart",
        string? title = null,
        string? voice = null,
        BedSpec? bed = null) =>
        new(text, libraryId, stationName, defaultVoice, authoredRoot,
            BedDuckDb: -12.0, BedPadSeconds: 1.5, Title: title, Voice: voice, Bed: bed);

    // ---------------------------------------------------------------------
    // HAPPY PATH — voice-only authoring
    // ---------------------------------------------------------------------

    public sealed class ScenarioVoiceOnlyAuthoringProducesAReadyRow : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task AFileIsWrittenUnderAuthoredRoot()
        {
            // AC1 — artifact path starts with Station:Safe:AuthoredRoot
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            var result = await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(mixer.LastRequest);
            Assert.StartsWith(authoredRoot, mixer.LastRequest!.OutputPath);
            Assert.True(File.Exists(mixer.LastRequest.OutputPath));
        }

        [Fact]
        public async Task AReadyMeasurableRowReferencesTheArtifact()
        {
            // AC1 — insert seam called with state-ready inputs referencing the file
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.NotNull(writer.LastInsert);
            Assert.Equal(mixer.LastRequest!.OutputPath, writer.LastInsert!.Path);
            Assert.True(writer.LastInsert.Loudness.Measurable);
        }

        [Fact]
        public async Task LoudnessCueAndEnergyArePopulatedFromTheMeasurement()
        {
            // AC1 — the three analyzer results flow into the insert
            cue.Returns(new CuePoints(0.0, 3.0));
            energy.Returns(new EnergyPoints(0.2, 0.3));

            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.Equal(loudness.Loudness, writer.LastInsert!.Loudness);
            Assert.Equal(new CuePoints(0.0, 3.0), writer.LastInsert.Cue);
            Assert.Equal(new EnergyPoints(0.2, 0.3), writer.LastInsert.Energy);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — bed authoring measures the MIXED output
    // ---------------------------------------------------------------------

    public sealed class ScenarioBedAuthoringMeasuresTheMixedOutput : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task AnalyzersRunAgainstTheMixedArtifactPath()
        {
            // AC2 — analyzers receive the mixer's output path, never the dry voice path
            var bed = new BedSpec("/media/jingle.wav", CueInSec: null, CueOutSec: null);
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot, bed: bed), CancellationToken.None);

            var artifactPath = mixer.LastRequest!.OutputPath;
            Assert.Equal(bed, mixer.LastRequest.Bed);
            Assert.Equal(artifactPath, loudness.LastPath);
            Assert.Equal(artifactPath, cue.LastPath);
            Assert.Equal(artifactPath, energy.LastPath);
            Assert.NotEqual(synth.LastReturnedPath, artifactPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — defaults
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultsApply : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task TitleDefaultsToPleaseStandBy()
        {
            // AC3 — no title in the request -> "Please Stand By"
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            await author.AuthorAsync(Request(authoredRoot, title: null), CancellationToken.None);

            Assert.Equal("Please Stand By", writer.LastInsert!.Tags.Title);
        }

        [Fact]
        public async Task VoiceDefaultsToStationVoice()
        {
            // AC3 — no voice in the request -> Station:Voice (the request's DefaultVoice field,
            // resolved by the caller since this project cannot read StationOptions itself)
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            await author.AuthorAsync(Request(authoredRoot, defaultVoice: "af_heart", voice: null), CancellationToken.None);

            Assert.Equal("af_heart", synth.LastVoice);
        }

        [Fact]
        public async Task ArtistIsAlwaysTheStationName()
        {
            // AC3 — artist == Station:Name regardless of request contents
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);
            await author.AuthorAsync(Request(authoredRoot, stationName: "GWAV 108.8"), CancellationToken.None);

            Assert.Equal("GWAV 108.8", writer.LastInsert!.Tags.Artist);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — all-or-nothing
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailuresLeaveNothingBehind : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeAudioMixer mixer = new();
        readonly FakeLoudnessAnalyzer loudness = new();
        readonly FakeCueAnalyzer cue = new();
        readonly FakeEnergyAnalyzer energy = new();
        readonly FakeAuthoredCatalogWriter writer = new();

        [Fact]
        public async Task SynthesisFailureInsertsNoRow()
        {
            // AC4 — Kokoro unreachable -> reported failure, insert seam never called
            synth.ThrowOnNextCall = new IOException("kokoro down");
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            var result = await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(SafeSegmentFailureReason.SynthesisFailed, result.FailureReason);
            Assert.Equal(0, writer.Calls);
        }

        [Fact]
        public async Task SynthesisFailureLeavesNoOrphanFile()
        {
            // AC4 — nothing remains under AuthoredRoot
            synth.ThrowOnNextCall = new IOException("kokoro down");
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.True(!Directory.Exists(authoredRoot) || Directory.GetFiles(authoredRoot).Length == 0);
        }

        [Fact]
        public async Task InsertFailureDeletesTheWrittenArtifact()
        {
            // AC5 — mix succeeded, insert failed -> file deleted, failure reported
            writer.ThrowOnNextCall = new InvalidOperationException("fk violation: unknown library_id");
            var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

            var result = await author.AuthorAsync(Request(authoredRoot), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(SafeSegmentFailureReason.InsertFailed, result.FailureReason);
            Assert.False(File.Exists(mixer.LastRequest!.OutputPath));
            Assert.False(File.Exists(synth.LastReturnedPath));
        }

        [Fact]
        public async Task UnwritableAuthoredRootFailsWithoutThrowing()
        {
            // P6 reviewer follow-up: Directory.CreateDirectory used to sit outside the guarded
            // pipeline, so an unwritable/invalid AuthoredRoot escaped as a raw exception (framework
            // 500) instead of this seam's promised typed failure. A regular file already occupying
            // the AuthoredRoot path makes Directory.CreateDirectory throw IOException.
            var blockingFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await File.WriteAllTextAsync(blockingFile, "not a directory");
            try
            {
                var author = BuildAuthor(synth, mixer, loudness, cue, energy, writer);

                var result = await author.AuthorAsync(Request(blockingFile), CancellationToken.None);

                Assert.False(result.Succeeded);
                Assert.Equal(SafeSegmentFailureReason.MixFailed, result.FailureReason);
                Assert.Equal(0, writer.Calls);
            }
            finally
            {
                File.Delete(blockingFile);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
