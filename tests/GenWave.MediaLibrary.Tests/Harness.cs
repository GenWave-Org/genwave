using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Mood;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Tests;

/// <summary>Factories + small query helpers shared by the integration tests.</summary>
static class Harness
{
    public static readonly DateTime Mtime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static MediaRepository Repo(DatabaseFixture f, Channel<long>? enrichQueue = null) =>
        new(f.DataSource, NullLogger<MediaRepository>.Instance, enrichQueue ?? Channel.CreateUnbounded<long>());

    /// <summary>
    /// Builds a <see cref="ScanService"/> against a real repository/media root. <paramref name="missThreshold"/>
    /// defaults to 1 — the pre-F58 single-miss behavior — so every pre-existing spec built on this
    /// factory keeps passing byte-for-byte; Story155's grace specs pass the threshold they need to
    /// exercise explicitly. <paramref name="logger"/> defaults to a no-op logger; Story155's
    /// deferred-miss-is-logged spec passes a <c>CapturingLogger</c>.
    /// </summary>
    public static (ScanService scan, Channel<long> queue) Scanner(
        MediaRepository repo, string mediaRoot, int missThreshold = 1,
        ILogger<ScanService>? logger = null)
    {
        var queue = Channel.CreateUnbounded<long>();
        var scan = new ScanService(repo, queue,
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { MediaRoot = mediaRoot }),
            logger ?? NullLogger<ScanService>.Instance,
            new FakeOptionsMonitor<ScanOptions>(new ScanOptions { MissThreshold = missThreshold }));
        return (scan, queue);
    }

    /// <summary>Default, disabled-by-nothing year lookup options — the kill switch stays on (Enabled=true)
    /// so a caller not testing F48 doesn't silently no-op a backfill tick that reaches this far.</summary>
    static IOptionsMonitor<YearLookupOptions> DefaultYearLookupOptions() =>
        new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions());

    public static EnrichmentService Enrichment(MediaRepository repo) =>
        new(repo,
            new Enricher(
                new FfmpegLoudnessAnalyzer(),
                new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions())),
                new FakeEnergyAnalyzer(),
                new FakeBpmAnalyzer(),
                NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions())),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService with caller-supplied fake analyzers — used by Story018 specs that
    /// need to control what each analyzer returns without touching the filesystem.
    /// Uses a no-op energy analyzer (energy is not under test in Story018) and a no-op bpm analyzer.
    /// </summary>
    public static EnrichmentService EnrichmentWith(MediaRepository repo, ILoudnessAnalyzer loudness, ICueAnalyzer cue) =>
        new(repo,
            new Enricher(loudness, cue, new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cue,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService with caller-supplied fake analyzers including energy — used by
    /// Story033 specs that need to control what each analyzer returns. Uses a no-op bpm analyzer
    /// (bpm is not under test in Story033).
    /// </summary>
    public static EnrichmentService EnrichmentWith(MediaRepository repo, ILoudnessAnalyzer loudness, ICueAnalyzer cue, IEnergyAnalyzer energy) =>
        new(repo,
            new Enricher(loudness, cue, energy, new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cue,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            energy,
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService with caller-supplied fake analyzers including bpm — used by
    /// Story142 specs that need to control what the bpm analyzer returns.
    /// </summary>
    public static EnrichmentService EnrichmentWith(
        MediaRepository repo, ILoudnessAnalyzer loudness, ICueAnalyzer cue, IEnergyAnalyzer energy, IBpmAnalyzer bpm) =>
        new(repo,
            new Enricher(loudness, cue, energy, bpm, NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cue,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            energy,
            bpm,
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for backfill tests: real loudness path (unused for backfill)
    /// and caller-supplied fake cue analyzer for the backfill pass.
    /// </summary>
    public static EnrichmentService BackfillWith(MediaRepository repo, ICueAnalyzer cue) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), cue, new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cue,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for backfill tests with an explicit batch size.
    /// </summary>
    public static EnrichmentService BackfillWith(MediaRepository repo, ICueAnalyzer cue, int batchSize) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), cue, new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cue,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions { BackfillBatchSize = batchSize }),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for energy backfill tests: caller-supplied fake energy analyzer
    /// for the backfill pass; loudness, cue, and bpm analyzers are no-ops (already measured).
    /// </summary>
    public static EnrichmentService BackfillEnergyWith(MediaRepository repo, IEnergyAnalyzer energy) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), energy, new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            energy,
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for energy backfill tests with an explicit batch size.
    /// </summary>
    public static EnrichmentService BackfillEnergyWith(MediaRepository repo, IEnergyAnalyzer energy, int batchSize) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), energy, new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions { BackfillBatchSize = batchSize }),
            energy,
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for BPM backfill tests: caller-supplied fake bpm analyzer for
    /// the backfill pass; loudness, cue, and energy analyzers are no-ops (already measured). Mirrors
    /// <see cref="BackfillEnergyWith(MediaRepository, IEnergyAnalyzer)"/> exactly (SPEC F46.3).
    /// </summary>
    public static EnrichmentService BackfillBpmWith(MediaRepository repo, IBpmAnalyzer bpm) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), bpm, NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            bpm,
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for BPM backfill tests with an explicit batch size.
    /// </summary>
    public static EnrichmentService BackfillBpmWith(MediaRepository repo, IBpmAnalyzer bpm, int batchSize) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), bpm, NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions { BackfillBatchSize = batchSize }),
            new FakeEnergyAnalyzer(),
            bpm,
            new FakeYearLookup(),
            DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for year-lookup backfill tests: caller-supplied fake year
    /// lookup and options monitor for the backfill pass; loudness, cue, energy, and bpm analyzers are
    /// no-ops (already measured). Mirrors <see cref="BackfillBpmWith(MediaRepository, IBpmAnalyzer)"/>
    /// exactly (SPEC F48.3).
    /// </summary>
    public static EnrichmentService BackfillYearLookupWith(
        MediaRepository repo, IYearLookup yearLookup, IOptionsMonitor<YearLookupOptions>? yearLookupOptions = null) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            yearLookup,
            yearLookupOptions ?? DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for year-lookup backfill tests with an explicit batch size.
    /// </summary>
    public static EnrichmentService BackfillYearLookupWith(
        MediaRepository repo, IYearLookup yearLookup, int batchSize, IOptionsMonitor<YearLookupOptions>? yearLookupOptions = null) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions { BackfillBatchSize = batchSize }),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            yearLookup,
            yearLookupOptions ?? DefaultYearLookupOptions());

    /// <summary>
    /// Builds an EnrichmentService wired for mood-tag backfill tests (SPEC F85.2-F85.4, STORY-216,
    /// T72): a REAL <see cref="OllamaMoodTagger"/> backed by <paramref name="handler"/> (the Story187
    /// fake-<see cref="HttpMessageHandler"/> idiom — no test reaches the network) so the constrained-
    /// output parse runs against genuine HTTP response bodies, not a pre-parsed in-memory double.
    /// <paramref name="gate"/> defaults to allowed (<see cref="FakeLlmBatchGate"/>'s own default) so a
    /// fact not about F85.3 degradation doesn't need to think about it; <paramref name="logger"/>
    /// defaults to a no-op logger, swapped for a <see cref="CapturingLogger{T}"/> by the degradation
    /// skip-log fact. Loudness/cue/energy/bpm/year-lookup are all no-ops — this backfill pass is not
    /// under test.
    /// </summary>
    public static EnrichmentService BackfillMoodTagWith(
        MediaRepository repo, HttpMessageHandler handler,
        ILlmBatchGate? gate = null, ILogger<EnrichmentService>? logger = null) =>
        new(repo,
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            Channel.CreateUnbounded<long>(),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            logger ?? NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            DefaultYearLookupOptions(),
            new OllamaMoodTagger(
                new HttpClient(handler),
                new FakeOptionsMonitor<MoodTaggerOptions>(
                    new MoodTaggerOptions { Endpoint = "http://fake-llm", Model = "test-model" })),
            gate ?? new FakeLlmBatchGate());

    public static List<long> DrainIds(Channel<long> queue)
    {
        var ids = new List<long>();
        while (queue.Reader.TryRead(out var id)) ids.Add(id);
        return ids;
    }

    public static async Task<string?> StateOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "select state from library.media where id = @id", new { id });
    }

    /// <summary>
    /// A fully-populated <see cref="AuthoredMediaInsert"/> for STORY-076 authored-insert specs.
    /// Every parameter overrides a sensible default so a spec only names the field it's asserting on.
    /// </summary>
    public static AuthoredMediaInsert AuthoredInsert(
        string? path = null,
        long libraryId = 1L,
        AudioTags? tags = null,
        LoudnessMeasurement? loudness = null,
        CuePoints? cue = null,
        EnergyPoints? energy = null) =>
        new(
            Path: path ?? "/authored/probe.wav",
            Format: "wav",
            LibraryId: libraryId,
            SizeBytes: 12_345L,
            Mtime: Mtime,
            Tags: tags ?? new AudioTags(Artist: "Station Name", Title: "Please Stand By"),
            Loudness: loudness ?? new LoudnessMeasurement(-14.0, -1.0, true),
            Cue: cue ?? new CuePoints(0.5, 3.0),
            Energy: energy ?? new EnergyPoints(0.5, 0.5),
            DurationMs: 5_000,
            SampleRate: 44_100,
            Channels: 2,
            BitrateKbps: 1_000);

    public static async Task<(double? IntegratedLufs, double? TruePeakDbtp, bool? Measurable,
        double? CueInSec, double? CueOutSec, double? IntroEnergy, double? OutroEnergy)>
        MeasurementsOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(double?, double?, bool?, double?, double?, double?, double?)>(
            "select integrated_lufs, true_peak_dbtp, measurable, cue_in_sec, cue_out_sec, intro_energy, outro_energy " +
            "from library.media where id = @id", new { id });
    }

    public static async Task<(string? Title, string? Artist)> TagsOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(string?, string?)>(
            "select title, artist from library.media where id = @id", new { id });
    }

    public static async Task<DateTime?> TagsEditedAtOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<DateTime?>(
            "select tags_edited_at from library.media where id = @id", new { id });
    }

    public static async Task<(DateTime? CueAnalyzedAt, DateTime? EnergyAnalyzedAt, DateTime? BpmAnalyzedAt, DateTime? YearLookupAt, DateTime? YearLookupMissedAt)> AnalyzedAtOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(DateTime?, DateTime?, DateTime?, DateTime?, DateTime?)>(
            "select cue_analyzed_at, energy_analyzed_at, bpm_analyzed_at, year_lookup_at, year_lookup_missed_at " +
            "from library.media where id = @id", new { id });
    }

    public static async Task<int> CountMediaRowsAsync(DatabaseFixture f)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from library.media");
    }

    /// <summary>A fully-populated enrichment payload for catalog tests (measurable toggled).</summary>
    public static EnrichmentResult ReadyResult(bool measurable) =>
        new(DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: "t", Artist: "a", Album: "al", AlbumArtist: "aa", Genre: "g", TrackNo: 1, Year: 2020,
            IntegratedLufs: -14.0, TruePeakDbtp: -1.0, Measurable: measurable,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow);

    /// <summary>
    /// A fully-populated enrichment payload with overridable tag fields — for tests that need
    /// to seed rows with specific artist/genre/title values to verify filter predicates.
    /// </summary>
    public static EnrichmentResult ReadyResultWith(
        string? title  = "t",
        string? artist = "a",
        string? genre  = "g",
        int?    year   = 2020) =>
        new(DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: title ?? "t", Artist: artist ?? "a", Album: "al", AlbumArtist: "aa",
            Genre: genre ?? "g", TrackNo: 1, Year: year,
            IntegratedLufs: -14.0, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow);
}
