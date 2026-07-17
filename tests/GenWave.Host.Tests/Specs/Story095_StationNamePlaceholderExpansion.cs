// STORY-095 — {StationName} expands on the endpoint path (Epic R / SPEC F29.1–F29.2, gitea-#184)
//
// BDD specification — xUnit. R1 moved expansion into SafeSegmentAuthor.AuthorAsync — the single
// seam both POST /api/safe-segments and the boot seed call through — so SafeLoopSeeder no longer
// pre-expands. This is the endpoint-path expansion spec Story078/079 lacked (the hole gitea-#184 shipped
// through: the endpoint never expanded the token at all, only the seeder did).
//
// In-process fakes here wire the REAL SafeSegmentAuthor (not a scripted double) at every scenario
// that needs to observe expansion — a recording ITtsSynthesizer is the only seam that actually
// needs to be a fake, mirroring Story078's fixture pattern but assembled locally since this project
// does not reference GenWave.Tts.Tests' Fakes assembly.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Seeding;
using GenWave.Tts;
// This test project also references GenWave.Loudness (see the csproj comment), which brings the
// `GenWave.Loudness` namespace into scope and shadows the unqualified `Loudness` domain type name
// — the same collision SafeSegmentAuthor.cs works around with this identical alias.
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;
// `GenWave.Host.Options` (imported below) collides with `Microsoft.Extensions.Options.Options` on
// the bare name "Options" — same alias convention as Story012/Story013/Story036.
using ExtOptions = Microsoft.Extensions.Options.Options;

namespace GenWave.Host.Tests.Specs;

// ── Shared in-process fakes ───────────────────────────────────────────────────────────────────────

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that always returns the given value.</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Records the text/voice it was asked to synthesize; writes a placeholder file so the rest
/// of the real <see cref="SafeSegmentAuthor"/> pipeline (which stats the file) succeeds.</summary>
file sealed class RecordingTtsSynthesizer(string outputDirectory) : ITtsSynthesizer
{
    public string? LastText { get; private set; }
    public string? LastVoice { get; private set; }

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        LastText = text;
        LastVoice = voice;
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, [0]);
        return Task.FromResult(path);
    }
}

/// <summary>Writes a placeholder file to <see cref="AudioMixRequest.OutputPath"/> without invoking
/// ffmpeg — this spec is about the text seen by TTS, not the audio pipeline.</summary>
file sealed class NoOpAudioMixer : IAudioMixer
{
    public Task MixAsync(AudioMixRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath) ?? ".");
        File.WriteAllBytes(request.OutputPath, [1, 2, 3, 4]);
        return Task.CompletedTask;
    }
}

file sealed class FixedLoudnessAnalyzer : ILoudnessAnalyzer
{
    public Task<LoudnessMeasurement> AnalyzeAsync(string path, CancellationToken ct) =>
        Task.FromResult(new LoudnessMeasurement(-16.0, -1.0, true));
}

file sealed class NullCueAnalyzer : ICueAnalyzer
{
    public Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct) => Task.FromResult<CuePoints?>(null);
}

file sealed class NullEnergyAnalyzer : IEnergyAnalyzer
{
    public Task<EnergyPoints?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct) =>
        Task.FromResult<EnergyPoints?>(null);
}

file sealed class RecordingCatalogWriter : IAuthoredCatalogWriter
{
    public AuthoredMediaInsert? LastInsert { get; private set; }
    public long NextId { get; set; } = 42;

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct)
    {
        LastInsert = insert;
        return Task.FromResult(NextId);
    }
}

/// <summary>Assembles the REAL <see cref="SafeSegmentAuthor"/> with fakes at every I/O seam except
/// the recording synthesizer, whose output feeds every scenario's assertion.</summary>
file static class RealAuthorFactory
{
    public static (SafeSegmentAuthor Author, RecordingTtsSynthesizer Synth, RecordingCatalogWriter Writer) Build(
        string authoredRoot)
    {
        var synth = new RecordingTtsSynthesizer(Path.Combine(authoredRoot, "synth"));
        var writer = new RecordingCatalogWriter();
        var author = new SafeSegmentAuthor(
            synth,
            new NoOpAudioMixer(),
            new FixedLoudnessAnalyzer(),
            new NullCueAnalyzer(),
            new NullEnergyAnalyzer(),
            writer,
            ExtOptions.Create(new TtsOptions { Format = "wav" }),
            NullLogger<SafeSegmentAuthor>.Instance);
        return (author, synth, writer);
    }
}

/// <summary><see cref="ILibraryRepository"/> stub that only knows the given ids — enough for
/// <see cref="SafeSegmentsController"/>'s libraryId validation.</summary>
file sealed class StubLibraryRepository(params long[] knownIds) : ILibraryRepository
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            ids.Where(knownIds.Contains).Select(id => new LibraryInfo(id, $"library-{id}")).ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>([]);
}

/// <summary><see cref="IAdminMediaLookup"/> that reads back whatever the real
/// <see cref="RecordingCatalogWriter"/> most recently inserted — mirrors production, where the
/// controller's post-render re-fetch reads the very row the catalog writer just wrote.</summary>
file sealed class BridgingAdminMediaLookup(RecordingCatalogWriter writer) : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
    {
        if (writer.LastInsert is not { } insert || id != writer.NextId)
            return Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(null);

        var dto = new AdminMediaDto(
            MediaId: id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Locator: insert.Path,
            Format: insert.Format,
            State: "ready",
            DurationMs: insert.DurationMs,
            Title: insert.Tags.Title,
            Artist: insert.Tags.Artist,
            Album: null,
            Genre: null,
            Year: null,
            IntegratedLufs: insert.Loudness.IntegratedLufs,
            TruePeakDbtp: insert.Loudness.TruePeakDbtp,
            Measurable: insert.Loudness.Measurable,
            CueInSec: insert.Cue?.CueInSec,
            CueOutSec: insert.Cue?.CueOutSec,
            Eligible: true,
            Version: "1");
        return Task.FromResult<(AdminMediaDto Row, long LibraryId)?>((dto, insert.LibraryId));
    }
}

file sealed class FakeMarkerStore : ISafeLoopSeedMarkerStore
{
    public Task<bool> ExistsAsync(CancellationToken ct) => Task.FromResult(false);
    public Task MarkCompletedAsync(CancellationToken ct) => Task.CompletedTask;
}

file sealed class FakeSeedSettingsStore : IStationSettingsStore
{
    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

/// <summary>Trimmed <see cref="ILibraryRepository"/>/<see cref="IAdminLibraryWrite"/> double covering
/// only what <see cref="SafeLoopSeeder"/>'s "create if absent" step needs (Story080's pattern).</summary>
file sealed class MinimalLibraryStore : ILibraryRepository, IAdminLibraryWrite
{
    readonly List<LibraryAdminInfo> libraries = [];
    long nextId = 1;

    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            libraries.Where(l => ids.Contains(l.Id)).Select(l => new LibraryInfo(l.Id, l.Name)).ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>(libraries.ToList());

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
    {
        var id = nextId++;
        libraries.Add(new LibraryAdminInfo(id, name, 0));
        return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.Created(id));
    }

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");
}

/// <summary>Records the request it was called with, without rendering anything — used where a
/// scenario only needs to prove what the seeder HANDS to the author, not what the author does with
/// it (Story080's <c>FakeSeedSafeSegmentAuthor</c> pattern, trimmed to just the recording half).</summary>
file sealed class RecordingSafeSegmentAuthor : ISafeSegmentAuthor
{
    public SafeSegmentRequest? LastRequest { get; private set; }

    public Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(SafeSegmentAuthorResult.Success(1));
    }
}

file static class SeederFactory
{
    public static (SafeLoopSeeder Seeder, FakeMarkerStore Marker, MinimalLibraryStore Libraries, FakeSeedSettingsStore Settings)
        Build(ISafeSegmentAuthor author, StationOptions stationOptions)
    {
        var marker = new FakeMarkerStore();
        var libraries = new MinimalLibraryStore();
        var settings = new FakeSeedSettingsStore();
        var seeder = new SafeLoopSeeder(
            marker, libraries, libraries, author, settings,
            new FakeOptionsMonitor<StationOptions>(stationOptions),
            NullLogger<SafeLoopSeeder>.Instance);
        return (seeder, marker, libraries, settings);
    }
}

/// <summary>Wires a real <see cref="SafeSegmentsController"/> to a real <see cref="SafeSegmentAuthor"/>
/// so <see cref="FeatureStationNamePlaceholderExpansion.ScenarioEndpointPathExpands"/> can observe the
/// endpoint path end to end. A separate file-local static class (not a method on the public Scenario
/// type) because its return type carries file-local fakes (CS9051).</summary>
file static class EndpointFixture
{
    public static (SafeSegmentsController Controller, RecordingTtsSynthesizer Synth) BuildController(string authoredRoot)
    {
        var (author, synth, writer) = RealAuthorFactory.Build(authoredRoot);
        var stationOptions = new StationOptions
        {
            Id = "test",
            Name = "Test Station",
            Voice = "af_heart",
            Safe = new StationSafeOptions
            {
                AuthoredRoot = authoredRoot,
                BedDuckDb = -12.0,
                BedPadSeconds = 1.5,
            },
        };
        var controller = new SafeSegmentsController(
            author,
            new StubLibraryRepository(1),
            new BridgingAdminMediaLookup(writer),
            new FakeOptionsMonitor<StationOptions>(stationOptions),
            new FakeOptionsMonitor<TtsOptions>(new TtsOptions()),
            NullLogger<SafeSegmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, synth);
    }
}

public static class FeatureStationNamePlaceholderExpansion
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEndpointPathExpands : IDisposable
    {
        // Arrange: real SafeSegmentsController -> real SafeSegmentAuthor -> a recording
        // ITtsSynthesizer; Station:Name = "Test Station"; POST body text carries the token.
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        const string TemplateText = "You're listening to {StationName}. We'll be right back.";

        [Fact]
        public async Task SynthesizedTextContainsTheStationName()
        {
            var (controller, synth) = EndpointFixture.BuildController(authoredRoot);

            await controller.Create(new SafeSegmentCreateRequest(TemplateText, 1), CancellationToken.None);

            Assert.Contains("Test Station", synth.LastText);
        }

        [Fact]
        public async Task SynthesizedTextContainsNoLiteralPlaceholder()
        {
            var (controller, synth) = EndpointFixture.BuildController(authoredRoot);

            await controller.Create(new SafeSegmentCreateRequest(TemplateText, 1), CancellationToken.None);

            Assert.DoesNotContain("{StationName}", synth.LastText);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
        }
    }

    public sealed class ScenarioExpansionLivesAtTheAuthor : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        [Fact]
        public async Task AuthorAsyncExpandsTheToken()
        {
            // Arrange: SafeSegmentAuthor.AuthorAsync directly with a SafeSegmentRequest whose Text
            // carries the token and StationName = "Test Station".
            var (author, synth, _) = RealAuthorFactory.Build(authoredRoot);
            var request = Request(authoredRoot, text: "You're listening to {StationName}.");

            await author.AuthorAsync(request, CancellationToken.None);

            Assert.Contains("Test Station", synth.LastText);
        }

        [Fact]
        public async Task SeederNoLongerPreExpands()
        {
            // The seeder passes Station:Safe:SeedMessage through verbatim — the raw template; the
            // author is the sole expansion point.
            var recorder = new RecordingSafeSegmentAuthor();
            var stationOptions = StationOptionsFor("You're listening to {StationName}.", authoredRoot);
            var (seeder, _, _, _) = SeederFactory.Build(recorder, stationOptions);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.NotNull(recorder.LastRequest);
            Assert.Contains("{StationName}", recorder.LastRequest.Text);
        }

        [Fact]
        public async Task SeededSegmentStillSpeaksTheExpandedName()
        {
            // Boot-seed path end-to-end (real author this time, not a recording double): the
            // rendered text still carries the real station name even though the seeder itself no
            // longer expands anything.
            var (author, synth, _) = RealAuthorFactory.Build(authoredRoot);
            var stationOptions = StationOptionsFor("You're listening to {StationName}.", authoredRoot);
            var (seeder, _, _, _) = SeederFactory.Build(author, stationOptions);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.Contains("Test Station", synth.LastText);
        }

        static SafeSegmentRequest Request(string authoredRoot, string text) => new(
            Text: text,
            LibraryId: 1,
            StationName: "Test Station",
            DefaultVoice: "af_heart",
            AuthoredRoot: authoredRoot,
            BedDuckDb: -12.0,
            BedPadSeconds: 1.5);

        static StationOptions StationOptionsFor(string seedMessage, string authoredRoot) => new()
        {
            Id = "test",
            Name = "Test Station",
            Voice = "af_heart",
            Safe = new StationSafeOptions
            {
                SeedMessage = seedMessage,
                AuthoredRoot = authoredRoot,
                BedDuckDb = -12.0,
                BedPadSeconds = 1.5,
            },
        };

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTextWithoutTokenPassesThrough : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        [Fact]
        public async Task PlainTextIsByteIdentical()
        {
            var (author, synth, _) = RealAuthorFactory.Build(authoredRoot);
            var request = new SafeSegmentRequest(
                Text: "No tokens here.",
                LibraryId: 1,
                StationName: "Test Station",
                DefaultVoice: "af_heart",
                AuthoredRoot: authoredRoot,
                BedDuckDb: -12.0,
                BedPadSeconds: 1.5);

            await author.AuthorAsync(request, CancellationToken.None);

            Assert.Equal("No tokens here.", synth.LastText);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
        }
    }
}
