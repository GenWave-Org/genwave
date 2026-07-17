// STORY-096 — Boot seed is distinguishable from manual segments (Epic R / SPEC F29.3, gitea-#185)
//
// BDD specification — xUnit. R1: the boot seed passes an explicit Title
// "Please Stand By (Station Default)" (SafeLoopSeeder.SeedTitle) so a boot-seeded row reads
// distinctly from one an operator authors manually through POST /api/safe-segments, which still
// defaults to the plain "Please Stand By" (SafeSegmentAuthor.DefaultTitle, F27.3 unchanged).

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
// `GenWave.Host.Options` (imported above) collides with `Microsoft.Extensions.Options.Options` on
// the bare name "Options" — same alias convention as Story012/Story013/Story036/Story095.
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

/// <summary>Records the request it was called with, without rendering anything — enough to prove
/// what the seeder HANDS to the author (Story080's <c>FakeSeedSafeSegmentAuthor</c> pattern, trimmed
/// to just the recording half).</summary>
file sealed class RecordingSafeSegmentAuthor : ISafeSegmentAuthor
{
    public SafeSegmentRequest? LastRequest { get; private set; }

    public Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(SafeSegmentAuthorResult.Success(1));
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

/// <summary>Records the text/voice it was asked to synthesize; writes a placeholder file so the rest
/// of the real <see cref="SafeSegmentAuthor"/> pipeline (which stats the file) succeeds.</summary>
file sealed class RecordingTtsSynthesizer(string outputDirectory) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, [0]);
        return Task.FromResult(path);
    }
}

/// <summary>Writes a placeholder file to <see cref="AudioMixRequest.OutputPath"/> without invoking
/// ffmpeg — this spec is about the title on the created row, not the audio pipeline.</summary>
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

/// <summary>Wires a real <see cref="SafeSegmentsController"/> to a real <see cref="SafeSegmentAuthor"/>
/// so <see cref="FeatureBootSeedDistinctTitle.ScenarioManualDefaultUnchanged"/> can observe the title
/// the author actually defaults to, not a value a scripted double merely echoes back.</summary>
file static class EndpointFixture
{
    public static SafeSegmentsController BuildController(string authoredRoot)
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
        return new SafeSegmentsController(
            author,
            new StubLibraryRepository(1),
            new BridgingAdminMediaLookup(writer),
            new FakeOptionsMonitor<StationOptions>(stationOptions),
            new FakeOptionsMonitor<TtsOptions>(new TtsOptions()),
            NullLogger<SafeSegmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}

public static class FeatureBootSeedDistinctTitle
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSeedTitle
    {
        // Arrange: SafeLoopSeeder with a recording fake ISafeSegmentAuthor (Story080 pattern),
        // no seed marker present.

        [Fact]
        public async Task SeedRequestCarriesTheStationDefaultTitle()
        {
            var recorder = new RecordingSafeSegmentAuthor();
            var stationOptions = new StationOptions
            {
                Id = "test",
                Name = "Test Station",
                Voice = "af_heart",
                Safe = new StationSafeOptions { AuthoredRoot = "/authored", BedDuckDb = -12.0, BedPadSeconds = 1.5 },
            };
            var (seeder, _, _, _) = SeederFactory.Build(recorder, stationOptions);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal("Please Stand By (Station Default)", recorder.LastRequest?.Title);
        }
    }

    public sealed class ScenarioManualDefaultUnchanged : IDisposable
    {
        readonly string authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        [Fact]
        public async Task PostWithoutTitleDefaultsToPlainPleaseStandBy()
        {
            // POST /api/safe-segments with no title (Story079 fixture).
            var controller = EndpointFixture.BuildController(authoredRoot);

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var created = Assert.IsType<CreatedResult>(result);
            var row = Assert.IsType<AdminMediaDto>(created.Value);
            Assert.Equal("Please Stand By", row.Title);
        }

        public void Dispose()
        {
            if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
        }
    }
}
