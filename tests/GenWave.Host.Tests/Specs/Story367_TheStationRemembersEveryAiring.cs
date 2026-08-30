// STORY-367 — The station remembers every airing (SPEC F149.1–F149.3 · PLAN T354, T355)
//
// BDD specification — xUnit. AC1-AC4, AC7, AC8 are WIRED at T355 — every one of them drives the REAL
// production binary (WebApplicationFactory<Program> over an ephemeral station+library Postgres, the
// Story345/Story366/Story343_AnnouncementLifecycleSmoke.cs factory idiom): publish a genuine
// TrackAired through the REAL, container-resolved IStationEventSink (which now composes
// MediaRotationEventSink, SPEC F149.2, PLAN T355), then drain the real queue through the real,
// container-resolved MediaRotationDrainService.ProcessAsync — the SAME "directly-testable seam,
// hosted loop removed" posture AnnouncementAiredDrainService.ProcessAsync already establishes one
// seam over (Story343_AnnouncementLifecycleSmoke.cs, Story345_PaWireProof.cs). AC5/AC6 (this file's
// own ScenarioTheLedgerIsSeededOnceFromTheSurvivingBoothLog/ScenarioSeedingIsIdempotent) stay exactly
// as T354 wired them: raw SQL + db/41-gardener-migration.sh, no production sink involved.

using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheStationRemembersEveryAiring
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — every music airing lands in the ledger, nothing else does
    // ---------------------------------------------------------------------

    [Collection(RotationHappyPathCollection.Name)]
    public sealed class ScenarioAnAiringIncrementsTheLedger(RotationHappyPathArc arc)
    {
        // Given a ready music row with no media_rotation row, When a TrackAired event for it
        // reaches the station event sinks.
        [Fact]
        public void TheLedgerRowExistsWithPlayCountOne() => Assert.Equal(1, arc.PlayCountAfterFirstAiring);
    }

    [Collection(RotationHappyPathCollection.Name)]
    public sealed class ScenarioFirstAndLastAiredStamps(RotationHappyPathArc arc)
    {
        // Given a row whose ledger says play_count 1, first_aired_at T1, When it airs again at T2.
        [Fact]
        public void PlayCountIsTwo() => Assert.Equal(2, arc.PlayCountAfterSecondAiring);

        [Fact]
        public void FirstAiredAtIsStillTOne() => Assert.Equal(arc.FirstAiring, arc.FirstAiredAtAfterSecondAiring);

        [Fact]
        public void LastAiredAtIsTTwo() => Assert.Equal(arc.SecondAiring, arc.LastAiredAtAfterSecondAiring);
    }

    [Collection(RotationHappyPathCollection.Name)]
    public sealed class ScenarioTheMediaRowsETagSurvivesAnAiring(RotationHappyPathArc arc)
    {
        // Given a media row with a known xmin, When it airs.
        [Fact]
        public void TheMediaRowsXminIsUnchanged() => Assert.Equal(arc.XminBeforeAnyAiring, arc.XminAfterBothAirings);
    }

    [Collection(RotationNonMusicCollection.Name)]
    public sealed class ScenarioNonMusicNeverTouchesTheLedger(RotationNonMusicArc arc)
    {
        // Given a break of idents, patter, crosstalk, an announcement, AND a gh-#99 safe-loop airing
        // (numeric MediaId, SegmentKind null — indistinguishable from music to MediaRotationEventSink's
        // own filter), When every one of them airs.
        [Fact]
        public void MediaRotationIsByteIdenticalBeforeAndAfter() =>
            Assert.Equal(arc.SnapshotBeforeBreak, arc.SnapshotAfterBreak);
    }

    public sealed class ScenarioTheLedgerIsSeededOnceFromTheSurvivingBoothLog(LedgerSeedArc arc)
        : IClassFixture<LedgerSeedArc>
    {
        // Given a booth log with N track-started rows for media 42 (min T_first, max T_last) and
        // no ledger, When the migration runs.
        [Fact]
        public void PlayCountIsN() => Assert.Equal(3, arc.PlayCount);

        [Fact]
        public void FirstAiredAtIsTFirst() => Assert.Equal(arc.FirstOccurredAt, arc.LedgerFirstAiredAt);

        [Fact]
        public void LastAiredAtIsTLast() => Assert.Equal(arc.LastOccurredAt, arc.LedgerLastAiredAt);
    }

    public sealed class ScenarioSeedingIsIdempotent(SeedIdempotencyArc arc) : IClassFixture<SeedIdempotencyArc>
    {
        // Given a seeded ledger, When the migration runs again.
        [Fact]
        public void EveryLedgerRowIsUnchanged() => Assert.Equal(arc.FirstRun, arc.SecondRun);
    }

    [Collection(RotationEpochCollection.Name)]
    public sealed class ScenarioTheLedgerNamesItsOwnEpoch(RotationEpochArc arc)
    {
        // Given a migrated station, When Gardener:RotationSince is read.
        [Fact]
        public void ItIsTheMigrationTimestamp() => Assert.InRange(
            arc.RotationSince, arc.BeforeMigration.AddSeconds(-2), arc.AfterMigration.AddSeconds(2));

        [Fact]
        public void EveryNeverAiredCountIsReturnedBesideIt() =>
            Assert.Equal(RotationEpochArc.NeverAiredMediaCount, arc.NeverAiredCount);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a ledger failure never touches air
    // ---------------------------------------------------------------------

    [Collection(RotationFaultCollection.Name)]
    public sealed class ScenarioALedgerWriteFailureNeverDelaysAir(RotationFaultArc arc)
    {
        // Given a ledger repository that throws, When a TrackAired event is published — compared
        // against a happy-path Publish measured in the SAME arc/factory (LOW-2, T355 review): a bare
        // "under 500ms" bound couldn't distinguish "unaffected by the fault" from "just always this
        // slow", since Publish() never calls IMediaRotationSink synchronously in the first place
        // (only MediaRotationDrainService.ProcessAsync does, timed separately, off the hot path).
        [Fact]
        public void PublishStaysWithinASmallFactorOfTheSameArcsHappyPathBaseline() =>
            Assert.True(
                arc.PublishElapsed <= (arc.HappyPathPublishElapsed * 5) + TimeSpan.FromMilliseconds(20),
                $"publish took {arc.PublishElapsed} vs a same-arc happy-path baseline of {arc.HappyPathPublishElapsed}");

        [Fact]
        public void ExactlyOneWarnNamesTheLedger()
        {
            var warning = Assert.Single(arc.CapturedWarnings);
            Assert.Contains("ledger", warning, StringComparison.OrdinalIgnoreCase);
        }
    }
}

// ── Collection definitions — one ephemeral Postgres/factory per Scenario group (the Story345/Story366
// "each Scenario group arranges its own Postgres exactly once" idiom, via ICollectionFixture<T> so
// xUnit builds the Arc once and shares it across every [Fact] class in that collection). ──────────────

[CollectionDefinition(Name)]
public sealed class RotationHappyPathCollection : ICollectionFixture<RotationHappyPathArc>
{
    public const string Name = "Story367RotationHappyPath";
}

[CollectionDefinition(Name)]
public sealed class RotationNonMusicCollection : ICollectionFixture<RotationNonMusicArc>
{
    public const string Name = "Story367RotationNonMusic";
}

[CollectionDefinition(Name)]
public sealed class RotationEpochCollection : ICollectionFixture<RotationEpochArc>
{
    public const string Name = "Story367RotationEpoch";
}

[CollectionDefinition(Name)]
public sealed class RotationFaultCollection : ICollectionFixture<RotationFaultArc>
{
    public const string Name = "Story367RotationFault";
}

// ── Arc fixtures — each arranges its own ephemeral Postgres + production host exactly ONCE
// (IAsyncLifetime.InitializeAsync) and captures every value its Facts read before tearing both down. ──

/// <summary>
/// AC1-AC3: one ready music row, aired twice through the REAL production IStationEventSink/
/// MediaRotationDrainService — proves the ledger increments, first/last-aired stamps behave, and the
/// media row's own xmin survives both airings (F149.1).
/// </summary>
public sealed class RotationHappyPathArc : IAsyncLifetime
{
    public DateTimeOffset FirstAiring { get; } = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
    public DateTimeOffset SecondAiring { get; } = DateTimeOffset.Parse("2026-08-02T12:00:00Z");

    public long PlayCountAfterFirstAiring { get; private set; }
    public long PlayCountAfterSecondAiring { get; private set; }
    public DateTimeOffset FirstAiredAtAfterSecondAiring { get; private set; }
    public DateTimeOffset LastAiredAtAfterSecondAiring { get; private set; }
    public string XminBeforeAnyAiring { get; private set; } = "";
    public string XminAfterBothAirings { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story355StationDatabase is file-local (CS9051), see
        // GardenerSeedTestDatabase's own remarks for the identical reason.
        await using var database = await Story355StationDatabase.StartAsync();

        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/rotation-happy-path.flac");
        XminBeforeAnyAiring = await GardenerSeedFixtures.ReadMediaXminAsync(database.LibraryConnectionString, mediaId);

        await using var factory = new Story355WebFactory(database);
        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        var reader = factory.Services.GetRequiredService<ChannelReader<MediaRotationAiredSignal>>();
        var drain = factory.Services.GetRequiredService<MediaRotationDrainService>();

        sink.Publish(new TrackAired(mediaId.ToString(), "A Song", "An Artist", 0.0, FirstAiring, 180_000));
        if (!reader.TryRead(out var firstSignal))
            throw new InvalidOperationException("the rotation queue carried no signal after the first TrackAired publish");
        await drain.ProcessAsync(firstSignal!, CancellationToken.None);

        var afterFirst = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the first airing");
        PlayCountAfterFirstAiring = afterFirst.PlayCount;

        sink.Publish(new TrackAired(mediaId.ToString(), "A Song", "An Artist", 0.0, SecondAiring, 180_000));
        if (!reader.TryRead(out var secondSignal))
            throw new InvalidOperationException("the rotation queue carried no signal after the second TrackAired publish");
        await drain.ProcessAsync(secondSignal!, CancellationToken.None);

        var afterSecond = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the second airing");
        PlayCountAfterSecondAiring = afterSecond.PlayCount;
        FirstAiredAtAfterSecondAiring = afterSecond.FirstAiredAt ?? throw new InvalidOperationException("first_aired_at was null");
        LastAiredAtAfterSecondAiring = afterSecond.LastAiredAt ?? throw new InvalidOperationException("last_aired_at was null");

        XminAfterBothAirings = await GardenerSeedFixtures.ReadMediaXminAsync(database.LibraryConnectionString, mediaId);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC4: an ident, a patter item, a crosstalk exchange, an announcement, AND a gh-#99 safe-loop airing
/// (HIGH-2, T355 review) all air through the REAL production IStationEventSink — library.media_rotation
/// must stay byte-identical (a full-table snapshot compared before and after), since none of these is a
/// rateable music row. The first four carry a non-null SegmentKind, so MediaRotationEventSink's own
/// filter refuses them before the queue is ever touched — that half of AC4 was already covered. The
/// fifth is the finding this arc was missing: the safe loop's REAL shape (numeric MediaId, SegmentKind
/// null — GET /internal/safe-track + PlayoutFeeder's own stamping, see MediaRotationEventSink's remarks)
/// passes that SAME filter exactly like genuine music, so only MediaRotationRepository's own gh-#99
/// exclusion (HIGH-1) can keep it out of the ledger — this arc drains it through the real
/// MediaRotationDrainService.ProcessAsync path (not just Publish) to prove the write itself is refused,
/// not merely that Publish() declined to enqueue it.
/// </summary>
public sealed class RotationNonMusicArc : IAsyncLifetime
{
    public IReadOnlyList<GardenerSeedFixtures.LedgerRow> SnapshotBeforeBreak { get; private set; } = [];
    public IReadOnlyList<GardenerSeedFixtures.LedgerRow> SnapshotAfterBreak { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var database = await Story355StationDatabase.StartAsync();

        // A REAL library.library row (Gh099_SafeContentRatingRepository.cs's own SeedAsync precedent)
        // — library_id has an FK to library.library(id), so the safe media row below needs a genuine
        // row to reference, and Station:SafeScope:LibraryIds needs a REAL id to name, not a placeholder.
        var safeLibraryId = await GardenerSeedFixtures.InsertLibraryAsync(database.LibraryConnectionString, "story367-nonmusic-safe");
        var safeMediaId = await GardenerSeedFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, "/test/rotation-nonmusic-safe.flac", safeLibraryId);

        await using var factory = new Story355WebFactory(database, safeLibraryId);
        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        var reader = factory.Services.GetRequiredService<ChannelReader<MediaRotationAiredSignal>>();
        var drain = factory.Services.GetRequiredService<MediaRotationDrainService>();

        SnapshotBeforeBreak = await GardenerSeedFixtures.SnapshotRotationTableAsync(database.LibraryConnectionString);

        // Ident (SegmentKind.StationId), patter (SegmentKind.BackAnnounce), crosstalk
        // (SegmentKind.Crosstalk), and an announcement (AnnouncementMediaId.Wrap, F144.1) — every
        // non-music SegmentKind this station's playout actually stamps.
        sink.Publish(new TrackAired("tts:ident-1", null, null, 0.0, DateTimeOffset.UtcNow, 1_200, SegmentKind: SegmentKind.StationId));
        sink.Publish(new TrackAired("tts:patter-1", null, null, 0.0, DateTimeOffset.UtcNow, 4_500, SegmentKind: SegmentKind.BackAnnounce));
        sink.Publish(new TrackAired("tts:crosstalk:asset-1", null, null, 0.0, DateTimeOffset.UtcNow, 6_000, SegmentKind: SegmentKind.Crosstalk));
        var announcementMediaId = AnnouncementMediaId.Wrap(999, "tts:announcement-hash");
        sink.Publish(new TrackAired(announcementMediaId, "Dinner's ready", null, 0.0, DateTimeOffset.UtcNow, 4_200, SegmentKind: SegmentKind.Announcement));

        // The fifth publish (HIGH-2) — the safe loop's own real shape. Reaches the queue exactly like
        // a genuine music TrackAired would (both filters in MediaRotationEventSink.Publish pass), so it
        // is drained through the same real MediaRotationDrainService.ProcessAsync AC1-AC3 already
        // exercise, never just published and left unread.
        sink.Publish(new TrackAired(safeMediaId.ToString(), null, null, 0.0, DateTimeOffset.UtcNow, 30_000, SegmentKind: null));
        if (!reader.TryRead(out var safeSignal))
            throw new InvalidOperationException("the rotation queue carried no signal after the safe-loop TrackAired publish");
        await drain.ProcessAsync(safeSignal!, CancellationToken.None);

        SnapshotAfterBreak = await GardenerSeedFixtures.SnapshotRotationTableAsync(database.LibraryConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC7: db/41-gardener-migration.sh's one-shot seed stamps Gardener:RotationSince once — read back
/// through the REAL, container-resolved IMediaRotationSink (never a raw settings-table query), beside
/// the never-aired count for the media rows this arc inserts with no airing at all.
/// </summary>
public sealed class RotationEpochArc : IAsyncLifetime
{
    public const int NeverAiredMediaCount = 3;

    public DateTimeOffset BeforeMigration { get; private set; }
    public DateTimeOffset AfterMigration { get; private set; }
    public DateTimeOffset RotationSince { get; private set; }
    public long NeverAiredCount { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story355StationDatabase.StartAsync();

        for (var i = 0; i < NeverAiredMediaCount; i++)
            await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, $"/test/rotation-epoch-{i}.flac");

        BeforeMigration = DateTimeOffset.UtcNow;
        database.RunFileInContainer(Path.Combine(GardenerSeedFixtures.RepoRoot(), "db", "41-gardener-migration.sh"));
        AfterMigration = DateTimeOffset.UtcNow;

        await using var factory = new Story355WebFactory(database);
        var ledger = factory.Services.GetRequiredService<IMediaRotationSink>();

        RotationSince = await ledger.GetRotationSinceAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("expected Gardener:RotationSince to be stamped after the migration ran");
        NeverAiredCount = await ledger.GetNeverAiredCountAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC8: substitutes a throwing IMediaRotationSink so the drain's own write genuinely fails — proves
/// Publish itself (the feeder's own hot path) never blocks on it, and that the failure surfaces as
/// exactly one WARN naming the ledger rather than a crash or a silent drop. No ephemeral Postgres is
/// provisioned here at all: the throwing fake never reaches a real connection, so this arc boots the
/// production binary DB-free (the NoPasswordWebFactory precedent, Story164_FailClosedWithoutPassword.cs).
/// </summary>
public sealed class RotationFaultArc : IAsyncLifetime
{
    public TimeSpan PublishElapsed { get; private set; }
    public TimeSpan HappyPathPublishElapsed { get; private set; }
    public IReadOnlyList<string> CapturedWarnings { get; private set; } = [];

    WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        var logs = new CapturingWarningLoggerProvider();
        factory = new Story355FaultWebFactory(new ThrowingMediaRotationSink(), logs);

        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        var reader = factory.Services.GetRequiredService<ChannelReader<MediaRotationAiredSignal>>();
        var drain = factory.Services.GetRequiredService<MediaRotationDrainService>();

        // Same-arc happy-path baseline (LOW-2, T355 review) — a Publish call through the SAME real
        // CompositeStationEventSink fan-out, measured BEFORE this arc's own throwing write is ever
        // exercised below. Read away (never drained) so it never reaches ThrowingMediaRotationSink and
        // never pollutes ExactlyOneWarnNamesTheLedger's own single-warning count.
        var baselineStopwatch = Stopwatch.StartNew();
        sink.Publish(new TrackAired("7", "Baseline Song", "Baseline Artist", 0.0, DateTimeOffset.UtcNow, 180_000));
        baselineStopwatch.Stop();
        HappyPathPublishElapsed = baselineStopwatch.Elapsed;
        reader.TryRead(out _);

        var stopwatch = Stopwatch.StartNew();
        sink.Publish(new TrackAired("42", "Some Song", "Some Artist", 0.0, DateTimeOffset.UtcNow, 180_000));
        stopwatch.Stop();
        PublishElapsed = stopwatch.Elapsed;

        if (!reader.TryRead(out var signal))
            throw new InvalidOperationException("the rotation queue carried no signal after the TrackAired publish");

        // The throw happens IN HERE, never on the Publish call above — ProcessAsync's own try/catch
        // (mirroring AnnouncementAiredDrainService.ProcessAsync) swallows it.
        await drain.ProcessAsync(signal!, CancellationToken.None);

        CapturedWarnings = logs.Messages;
    }

    public async Task DisposeAsync()
    {
        if (factory is not null)
            await factory.DisposeAsync();
    }
}

// ── Test doubles ───────────────────────────────────────────────────────────────────────────────────

/// <summary>AC8's own fault: every write throws, standing in for a real ledger write failure (a DB
/// outage, a constraint violation) without needing a real Postgres to produce one.</summary>
file sealed class ThrowingMediaRotationSink : IMediaRotationSink
{
    public Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct) =>
        throw new InvalidOperationException("simulated rotation ledger write failure (STORY-367 AC8)");

    public Task<DateTimeOffset?> GetRotationSinceAsync(CancellationToken ct) =>
        throw new NotSupportedException("unused by this arc");

    public Task<long> GetNeverAiredCountAsync(CancellationToken ct) =>
        throw new NotSupportedException("unused by this arc");

    public Task<RotationHealth> GetRotationHealthAsync(LibraryScope scope, CancellationToken ct) =>
        throw new NotSupportedException("unused by this arc");
}

/// <summary>Captures every Warning+ log entry's message text — the Story164_FailClosedWithoutPassword.cs
/// CapturingWarningLoggerProvider precedent, redefined here (file-scoped there too, T354 review LOW-3's
/// "no shared test-support project exists" acceptance applied again).</summary>
file sealed class CapturingWarningLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingWarningLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(formatter(state, exception));
        }
    }
}

// ── Test harness — WebApplicationFactory subclasses ───────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root (Program.cs) against a real ephemeral Postgres
/// (<see cref="Story355StationDatabase"/>) — mirrors SensorGateWebFactory's (Story366) own shape.
/// Hosted services are removed (no Liquidsoap/real-background-loop reach during this test); the
/// rotation drain service is re-registered as itself (AddHostedService&lt;T&gt; never exposes T for
/// direct resolution) purely so each Arc can call ProcessAsync directly — the Story345_PaWireProof.cs
/// precedent.
/// </summary>
/// <param name="safeLibraryId">
/// The gh-#99 <c>Station:SafeScope:LibraryIds</c> override (HIGH-1/HIGH-2, T355 review) — deliberately
/// NEVER left at appsettings.Development.json's own shipped default of <c>[1]</c>, since EVERY arc that
/// shares this factory (RotationHappyPathArc, RotationEpochArc, RotationNonMusicArc) inserts its own
/// music rows via <see cref="GardenerSeedFixtures.InsertMediaRowAsync"/>'s implicit <c>library_id</c>
/// default, which is ALSO 1: left unset, HIGH-1's own safe-scope exclusion in
/// <c>MediaRotationRepository.RecordAiringAsync</c>/<c>GetNeverAiredCountAsync</c> would silently
/// exclude every one of THOSE arcs' own rows too, breaking AC1/AC2/AC3/AC7. Defaults to a harmless,
/// non-colliding placeholder id that no arc ever inserts a row into; RotationNonMusicArc (HIGH-2) is
/// the one caller that supplies the REAL id of the safe library.library row it seeded, so its own
/// fifth publish's exclusion is genuinely exercised.
/// </param>
file sealed class Story355WebFactory(Story355StationDatabase db, long safeLibraryId = 999_999) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", "test-password-story367-rotation");

        // The exact four Station:* keys compose.yaml itself overrides in production (Story366's own
        // precedent) — every other Station:* leaf rides appsettings.json's own shipped default.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        // gh-#99 override — see this class's own <paramref name="safeLibraryId"/> remarks.
        builder.UseSetting("Station:SafeScope:LibraryIds:0", safeLibraryId.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<MediaRotationDrainService>();
        });
    }
}

/// <summary>
/// AC8's own DB-free factory: the throwing <see cref="IMediaRotationSink"/> never reaches a real
/// connection, so this factory boots against the SAME unreachable-but-syntactically-valid connection
/// string Story164_FailClosedWithoutPassword.cs's own NoPasswordWebFactory uses for
/// ConnectionStrings:Library (AddMediaLibrary requires a non-null value; nothing on this arc's path
/// ever opens it) — ConnectionStrings:Station stays empty, the documented "no station DB configured"
/// degrade path every station-schema store already honors, so no connection attempt happens there
/// either.
/// </summary>
file sealed class Story355FaultWebFactory(IMediaRotationSink rotationSink, CapturingWarningLoggerProvider logs)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("ConnectionStrings:Station", "");
        // Set (not left absent) so this arc's own CapturingWarningLoggerProvider sees only the
        // ledger's own WARN — an absent Admin:Password logs its own unrelated fail-closed WARN at
        // boot (Story164_FailClosedWithoutPassword.cs's own subject), which would otherwise pollute
        // ExactlyOneWarnNamesTheLedger's count.
        builder.UseSetting("Admin:Password", "test-password-story367-rotation-fault");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<MediaRotationDrainService>();

            // Overrides AddMediaLibrary's own IMediaRotationSink registration — the last registration
            // wins for a single-resolve seam (SEAMS.md's own documented rule), exactly the substitution
            // AC8 needs.
            services.AddSingleton(rotationSink);
            services.AddSingleton<ILoggerProvider>(logs);
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// <see cref="GardenerSeedTestDatabase"/>'s own remarks for the full "which compose file, why a
/// unique project name + OS-assigned port" rationale. Supplies only the
/// <c>"genwave-rotation"</c> compose project-name prefix T355's own arcs need (distinct from
/// <c>GardenerSeedTestDatabase</c>'s <c>"genwave-gardenseed"</c> prefix so the two never collide).
/// </summary>
file sealed class Story355StationDatabase : EphemeralStationDatabase
{
    Story355StationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story355StationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-rotation");
        var db = new Story355StationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// A booth log with three <c>track-started</c> rows for one media row, and no ledger yet. Runs
/// db/41-gardener-migration.sh's one-shot seed exactly once and captures the resulting
/// <c>library.media_rotation</c> row alongside the raw booth-log timestamps the assertions compare
/// it against (STORY-367 AC5).
/// </summary>
public sealed class LedgerSeedArc : IAsyncLifetime
{
    public long PlayCount { get; private set; }
    public DateTimeOffset FirstOccurredAt { get; private set; }
    public DateTimeOffset LastOccurredAt { get; private set; }
    public DateTimeOffset LedgerFirstAiredAt { get; private set; }
    public DateTimeOffset LedgerLastAiredAt { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field: GardenerSeedTestDatabase is file-local (CS9051 forbids it in a
        // member signature of this public type), and every value this arc exposes is captured
        // into a property below before the container ever tears down — the same "await using var
        // db = ..." shape Story345_PaWireProof.cs's own arcs already use.
        await using var database = await GardenerSeedTestDatabase.StartAsync();

        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/gardener-seed-ac5.flac");

        FirstOccurredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var middle = DateTimeOffset.Parse("2026-08-10T00:00:00Z");
        LastOccurredAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z");

        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, FirstOccurredAt);
        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, middle);
        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, LastOccurredAt);

        database.RunFileInContainer(Path.Combine(GardenerSeedFixtures.RepoRoot(), "db", "41-gardener-migration.sh"));

        var row = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the seed migration ran");
        PlayCount = row.PlayCount;
        LedgerFirstAiredAt = row.FirstAiredAt ?? throw new InvalidOperationException("first_aired_at was null");
        LedgerLastAiredAt = row.LastAiredAt ?? throw new InvalidOperationException("last_aired_at was null");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Runs db/41-gardener-migration.sh's one-shot seed step TWICE against the same booth log and
/// captures the full <c>library.media_rotation</c> row after each run — the honest way to prove
/// "every ledger row is unchanged" (STORY-367 AC6) is a byte-for-byte row comparison, including
/// <c>updated_at</c> (an <c>on conflict ... do nothing</c> re-run must never touch it).
/// </summary>
public sealed class SeedIdempotencyArc : IAsyncLifetime
{
    public GardenerSeedFixtures.LedgerRow FirstRun { get; private set; }
    public GardenerSeedFixtures.LedgerRow SecondRun { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — see LedgerSeedArc's own remarks (CS9051, the file-local
        // GardenerSeedTestDatabase type).
        await using var database = await GardenerSeedTestDatabase.StartAsync();

        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/gardener-seed-ac6.flac");
        await GardenerSeedFixtures.InsertTrackStartedAsync(
            database.StationConnectionString, mediaId, DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var scriptPath = Path.Combine(GardenerSeedFixtures.RepoRoot(), "db", "41-gardener-migration.sh");

        database.RunFileInContainer(scriptPath);
        FirstRun = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the first seed run");

        database.RunFileInContainer(scriptPath);
        SecondRun = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the second seed run");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Arrange helpers shared by <see cref="LedgerSeedArc"/>, <see cref="SeedIdempotencyArc"/>,
/// and T355's own real-sink-driven arcs — raw SQL against the ephemeral database's own connection
/// strings, never through <see cref="GenWave.MediaLibrary.Garden.MediaRotationRepository"/> itself
/// (that would only prove the repository agrees with itself; these arcs need an independent read of
/// what actually landed in Postgres).</summary>
public static class GardenerSeedFixtures
{
    public readonly record struct LedgerRow(long PlayCount, DateTimeOffset? FirstAiredAt, DateTimeOffset? LastAiredAt, DateTimeOffset UpdatedAt);

    /// <param name="libraryId">
    /// gh-#99 (HIGH-2, T355 review): when supplied, the row lands in THIS library rather than the
    /// column's own default (1) — the shape a safe-scope-exclusion fixture needs (the row must
    /// reference a REAL <c>library.library</c> row, see <see cref="InsertLibraryAsync"/>). Every
    /// pre-existing caller omits it and keeps landing at the default, unaffected.
    /// </param>
    /// <remarks>
    /// Stamps <c>measurable = true</c> explicitly (LOW-1, T355 review) — that column has NO table
    /// default (unlike <c>eligible</c>, <c>not null default true</c>), so an omitted value reads back
    /// <see langword="null"/>, and <c>MediaRepository.PlayablePredicate</c>'s <c>and m.measurable</c>
    /// term is FALSE for a null boolean, not true: every row this fixture creates represents an
    /// already-enriched, ready-to-air music row for the ledger's own purposes, so leaving the real
    /// enrichment-pending default (null) here would silently drop every one of them out of
    /// <see cref="GenWave.MediaLibrary.Garden.MediaRotationRepository.GetNeverAiredCountAsync"/>'s own
    /// now-playable-scoped count.
    /// </remarks>
    public static async Task<long> InsertMediaRowAsync(string libraryConnectionString, string path, long? libraryId = null)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = libraryId is null
            ? """
              insert into library.media (path, format, size_bytes, mtime, state, measurable)
              values (@path, 'flac', 1024, now(), 'ready', true)
              returning id
              """
            : """
              insert into library.media (path, format, size_bytes, mtime, state, measurable, library_id)
              values (@path, 'flac', 1024, now(), 'ready', true, @libraryId)
              returning id
              """;
        cmd.Parameters.AddWithValue("path", path);
        if (libraryId is not null)
            cmd.Parameters.AddWithValue("libraryId", libraryId.Value);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    /// <summary>
    /// Inserts a fresh <c>library.library</c> row (gh-#99, HIGH-2, T355 review — the
    /// Gh099_SafeContentRatingRepository.cs "SeedAsync" precedent) — returns its generated id for use
    /// as both a media row's <c>library_id</c> (<see cref="InsertMediaRowAsync"/>) and a
    /// <c>Station:SafeScope:LibraryIds</c> override, so the two genuinely agree on which library is
    /// "safe" rather than relying on a placeholder id that happens not to collide.
    /// </summary>
    public static async Task<long> InsertLibraryAsync(string libraryConnectionString, string name)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into library.library (name) values (@name) returning id";
        cmd.Parameters.AddWithValue("name", name);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task InsertTrackStartedAsync(string stationConnectionString, long mediaId, DateTimeOffset occurredAt)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.booth_log (occurred_at, kind, summary, media_id)
            values (@occurredAt, 'track-started', 'seed fixture', @mediaId)
            """;
        cmd.Parameters.AddWithValue("occurredAt", occurredAt);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<LedgerRow?> ReadLedgerRowAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select play_count, first_aired_at, last_aired_at, updated_at from library.media_rotation where media_id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new LedgerRow(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>The row's current <c>xmin</c> (Postgres row version) for a <c>library.media</c> row,
    /// as text — STORY-367 AC3's own proof that an airing never bumps it (mirrors
    /// GenWave.MediaLibrary.Tests.Specs.Story040_AdminWriteRepoAndEligibilityFilter.cs's own
    /// ReadXminAsync helper, redefined here per this file's own "no shared test-support project"
    /// duplication-by-necessity precedent).</summary>
    public static async Task<string> ReadMediaXminAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select xmin::text from library.media where id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        return (string?)await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("expected a library.media row");
    }

    /// <summary>Every <c>library.media_rotation</c> row, in a stable order, for STORY-367 AC4's own
    /// byte-identical-before-and-after comparison.</summary>
    public static async Task<IReadOnlyList<LedgerRow>> SnapshotRotationTableAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select play_count, first_aired_at, last_aired_at, updated_at from library.media_rotation order by media_id";

        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<LedgerRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new LedgerRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return rows;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }
}

file sealed class GardenerSeedTestDatabase : EphemeralStationDatabase
{
    GardenerSeedTestDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<GardenerSeedTestDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-gardenseed");
        var db = new GardenerSeedTestDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
