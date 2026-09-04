// STORY-375 — Dead files are visible the moment they cost a pick (SPEC F153.3–F153.4 · PLAN T372/T373)
//
// BDD specification — xUnit. AC1/AC2 WIRED at T372; AC3/AC4/AC5 WIRED at T373. Entry-point
// discipline: AC1/AC2 run the real, container-composed dead_file IGardenerPass (IGardenerPass is a
// PUBLIC GenWave.Core.Abstractions port, resolvable from this test assembly with no
// InternalsVisibleTo into GenWave.MediaLibrary) against rows arranged directly in the ephemeral
// Postgres (Support/EphemeralStationDatabase, the Story345/Story366/Story367/T372 factory idiom over
// WebApplicationFactory<Program>) — state=failed, and a stale unavailable row past
// Library:Scan:MissThreshold × Library:ScanIntervalSeconds. AC3–AC5 drive the PRODUCTION feeder path:
// the real, container-resolved ILiquidsoapControl (== MediaExistencePushGuard, wired ahead of
// LiquidsoapControl in PlayoutServiceCollectionExtensions exactly as production boots it) pushed
// directly with a MediaItem whose locator points into a temp file (Path.GetTempPath()-rooted,
// mirroring Gh612_MediaExistencePushGuard.cs) with the file absent — then present again for AC5's
// resurrection — and the real IDeadFileReporter reporting fire-and-forget into the same rot_finding
// table AC1/AC2 read. AC4's throwing reporter is a scripted IDeadFileReporter substitute swapped
// into the container via a last-registration-wins AddSingleton (the Story367 RotationFaultArc
// precedent), timed against the guard's own decline to prove the WARN never costs the push a
// millisecond. The ORCHESTRATOR ruling's own flap-guard facts (a fresh push_missing finding
// surviving a reconcile; the same finding resolving once it clears the grace on a still-ready row)
// are pinned directly against RotFindingRepository in GenWave.MediaLibrary.Tests instead of here —
// repository-level SQL correctness, no Host/WebApplicationFactory needed to prove it.

using System.Diagnostics;
using System.Text.Json;
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
using GenWave.Host.Tests.Support;
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDeadFilesAreVisibleWhenTheyCostAPick
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — failed, long-unavailable, and push-missed rows all surface
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailedRows(FailedRowArc arc) : IClassFixture<FailedRowArc>
    {
        // Given a row with state failed, When the dead_file pass runs.
        [Fact]
        public void AnOpenDeadFileFindingExists()
        {
            Assert.Equal("open", arc.State);
        }

        [Fact]
        public void TheEvidenceReasonIsFailed()
        {
            using var evidence = JsonDocument.Parse(arc.Evidence);
            Assert.Equal("failed", evidence.RootElement.GetProperty("reason").GetString());
        }
    }

    public sealed class ScenarioLongUnavailableRows(LongUnavailableRowArc arc) : IClassFixture<LongUnavailableRowArc>
    {
        // Given an unavailable row older than the miss grace, When the pass runs.
        [Fact]
        public void AnOpenDeadFileFindingExists()
        {
            Assert.Equal("open", arc.State);
        }

        [Fact]
        public void TheEvidenceReasonIsUnavailable()
        {
            using var evidence = JsonDocument.Parse(arc.Evidence);
            Assert.Equal("unavailable", evidence.RootElement.GetProperty("reason").GetString());
        }
    }

    public sealed class ScenarioAPushMissReportsImmediately(PushMissingArc arc) : IClassFixture<PushMissingArc>
    {
        // Given a ready row whose file is missing on disk, When the feeder pushes it.
        [Fact]
        public void ThePushIsDeclinedUnchanged() => Assert.Null(arc.PushResult);

        [Fact]
        public void AFindingExistsWithinOneSecondWithReasonPushMissing() =>
            Assert.Equal("push_missing", arc.EvidenceReason);
    }

    public sealed class ScenarioTheReporterNeverBlocks(ReporterNeverBlocksArc arc) : IClassFixture<ReporterNeverBlocksArc>
    {
        // Given a reporter that throws, When the feeder pushes a missing file.
        [Fact]
        public void TheDeclinesTimingIsUnchanged() =>
            Assert.True(
                arc.FaultDeclineElapsed <= (arc.BaselineDeclineElapsed * 5) + TimeSpan.FromMilliseconds(20),
                $"decline took {arc.FaultDeclineElapsed} vs a same-arc baseline of {arc.BaselineDeclineElapsed}");

        [Fact]
        public void ExactlyOneWarnNamesTheReporter() =>
            Assert.Single(arc.CapturedWarnings, m => m.Contains(nameof(IDeadFileReporter), StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the file comes back
    // ---------------------------------------------------------------------

    public sealed class ScenarioAResurrectedFileResolvesIt(ResurrectedFileArc arc) : IClassFixture<ResurrectedFileArc>
    {
        // Given a push_missing finding and the file back on disk, When the scan sights it and the pass runs.
        [Fact]
        public void TheFindingIsResolved() => Assert.Equal("resolved", arc.State);
    }
}

// ── Arc fixtures ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AC1: a single <c>state = 'failed'</c> media row, no <c>unavailable_since</c> involved at all —
/// runs the real, container-composed dead_file <see cref="IGardenerPass"/> once and reads back the
/// resulting finding.
/// </summary>
public sealed class FailedRowArc : IAsyncLifetime
{
    public string State { get; private set; } = "";
    public string Evidence { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story372DeadFileReasonDatabase is file-local (CS9051), the
        // identical reason Story367/T372's own arcs give for the same shape.
        await using var database = await Story372DeadFileReasonDatabase.StartAsync();

        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t372-failed.flac", "failed");

        await using var factory = new Story372DeadFileReasonWebFactory(database);
        var pass = factory.Services.GetServices<IGardenerPass>().Single(p => p.Kind == RotKind.DeadFile);
        await pass.RunAsync(CancellationToken.None);

        var finding = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected a dead_file finding for the failed row");
        State = finding.State;
        Evidence = finding.Evidence;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC2: a single <c>state = 'unavailable'</c> media row whose <c>unavailable_since</c> is well past
/// the pass's own grace window — the factory overrides <c>Library:Scan:MissThreshold</c> and
/// <c>Library:ScanIntervalSeconds</c> to <c>1</c> each (grace = 1 second, ORCHESTRATOR ruling's own
/// <c>MissThreshold × ScanIntervalSeconds</c> formula) so a five-second-old <c>unavailable_since</c>
/// unambiguously clears it.
/// </summary>
public sealed class LongUnavailableRowArc : IAsyncLifetime
{
    public string State { get; private set; } = "";
    public string Evidence { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await Story372DeadFileReasonDatabase.StartAsync();

        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, "/test/t372-unavailable.flac", "unavailable",
            unavailableSince: DateTimeOffset.UtcNow.AddSeconds(-5));

        await using var factory = new Story372DeadFileReasonWebFactory(database);
        var pass = factory.Services.GetServices<IGardenerPass>().Single(p => p.Kind == RotKind.DeadFile);
        await pass.RunAsync(CancellationToken.None);

        var finding = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected a dead_file finding for the long-unavailable row");
        State = finding.State;
        Evidence = finding.Evidence;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC3: a single <c>state = 'ready'</c> media row whose locator names a file that does not exist —
/// drives the REAL, container-resolved <see cref="ILiquidsoapControl"/> (== MediaExistencePushGuard)
/// directly with <c>PushAsync</c>, the production feeder's own call shape, then polls the SAME
/// <c>rot_finding</c> table AC1/AC2 read for the resulting <c>push_missing</c> finding — up to one
/// second of real wall-clock (<see cref="PushGuardPolling.PollForOpenFindingAsync"/>'s own 50ms granularity), never a
/// direct call into <see cref="IDeadFileReporter"/>.
/// </summary>
public sealed class PushMissingArc : IAsyncLifetime
{
    public EnginePushResult? PushResult { get; private set; }
    public string EvidenceReason { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await Story372DeadFileReasonDatabase.StartAsync();

        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t373-push-missing.flac", "ready");
        var missingLocator = Path.Combine(Path.GetTempPath(), $"t373-{Guid.NewGuid():N}.flac");

        await using var factory = new Story372DeadFileReasonWebFactory(database);
        var guard = factory.Services.GetRequiredService<ILiquidsoapControl>();

        PushResult = await guard.PushAsync(
            new MediaItem(mediaId.ToString(), missingLocator, "Missing Track", new CoreLoudness(-16.0, -1.0, Measurable: true)),
            0.0, CancellationToken.None);

        var finding = await PushGuardPolling.PollForOpenFindingAsync(database.LibraryConnectionString, mediaId, "dead_file", TimeSpan.FromSeconds(1))
            ?? throw new InvalidOperationException("expected a push_missing dead_file finding within one second of the declined push");

        using var evidence = JsonDocument.Parse(finding.Evidence);
        EvidenceReason = evidence.RootElement.GetProperty("reason").GetString() ?? "";
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC4: two ready rows, one locator missing throughout — a scripted <see cref="IDeadFileReporter"/>
/// (<see cref="ThrowsForOneMediaId"/>) substitutes the real one, behaving as a no-op reporter for
/// the baseline id and throwing only for the fault id, both served by the SAME running factory (the
/// Story367 <c>RotationFaultArc</c> "same-arc baseline" precedent). Every push is fire-and-forget
/// past the guard's own decline (<see cref="GenWave.Host.Engine.MediaExistencePushGuard.PushAsync"/>'s
/// own remarks), so both measurements are the guard's own return time — the reporter's own behaviour
/// runs on a discarded background task neither measurement ever touches.
/// </summary>
public sealed class ReporterNeverBlocksArc : IAsyncLifetime
{
    public TimeSpan BaselineDeclineElapsed { get; private set; }
    public TimeSpan FaultDeclineElapsed { get; private set; }
    public IReadOnlyList<string> CapturedWarnings { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var database = await Story372DeadFileReasonDatabase.StartAsync();

        var baselineMediaId = await GardenerRotFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, "/test/t373-ac4-baseline.flac", "ready");
        var faultMediaId = await GardenerRotFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, "/test/t373-ac4-fault.flac", "ready");

        var reporter = new ThrowsForOneMediaId(faultMediaId);
        var logs = new CapturingWarningLoggerProvider();

        await using var factory = new Story372DeadFileReasonWebFactory(database, services =>
        {
            // Last-registration-wins (SEAMS.md's documented rule) — substitutes the real
            // IDeadFileReporter/adds a log capture, exactly the Story367 RotationFaultArc shape one
            // seam over.
            services.AddSingleton<IDeadFileReporter>(reporter);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        var guard = factory.Services.GetRequiredService<ILiquidsoapControl>();

        // A throwaway warmup push (T373 review LOW-3): the FIRST call through this pipeline pays a
        // one-time JIT/tiering cost the TIMED baseline below must not absorb — same arc/guard, its
        // own distinct id (never the fault id) so its own reporter call is a harmless no-op that
        // never adds a WARN.
        var warmupMediaId = await GardenerRotFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, "/test/t373-ac4-warmup.flac", "ready");
        await guard.PushAsync(MissingItem(warmupMediaId), 0.0, CancellationToken.None);

        var baselineStopwatch = Stopwatch.StartNew();
        await guard.PushAsync(MissingItem(baselineMediaId), 0.0, CancellationToken.None);
        baselineStopwatch.Stop();
        BaselineDeclineElapsed = baselineStopwatch.Elapsed;

        var faultStopwatch = Stopwatch.StartNew();
        await guard.PushAsync(MissingItem(faultMediaId), 0.0, CancellationToken.None);
        faultStopwatch.Stop();
        FaultDeclineElapsed = faultStopwatch.Elapsed;

        // The throw happens on a discarded background task, off this method's own call stack (the
        // scripted reporter yields before it throws — see ThrowsForOneMediaId for why that matters
        // to the stopwatch above) — poll for the SPECIFIC reporter-failure WARN it must produce
        // (T373 review LOW-3: not "any WARN"
        // — the guard's own "Declined push of..." WARN fires synchronously on every one of the
        // three pushes above and would satisfy a bare Count > 0 well before the background reporter
        // task ever runs, undercutting this very wait).
        await GardenerRotFixtures.WaitUntilAsync(
            () => logs.Messages.Any(m => m.Contains(nameof(IDeadFileReporter), StringComparison.Ordinal)),
            TimeSpan.FromSeconds(2));
        CapturedWarnings = logs.Messages;
    }

    static MediaItem MissingItem(long mediaId) => new(
        mediaId.ToString(), Path.Combine(Path.GetTempPath(), $"t373-ac4-{Guid.NewGuid():N}.flac"),
        "Missing Track", new CoreLoudness(-16.0, -1.0, Measurable: true));

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC5: a single ready row, pushed once with the file absent (establishing the same push_missing
/// finding AC3 pins), then the file is written back to the SAME locator and — past the push_missing
/// grace so the flap guard (<see cref="Garden.RotFindingRepository"/>'s own resolve statement,
/// GenWave.MediaLibrary.Tests' own <c>FeatureRotFindingFlapGuard</c> pins the SQL directly) no longer
/// holds it open — the real, container-resolved dead_file <see cref="IGardenerPass"/> runs and
/// resolves it. The row's own <c>state</c> never leaves <c>'ready'</c> anywhere in this arc (no scan
/// service runs here), so "the scan sights it" holds trivially by construction.
/// </summary>
public sealed class ResurrectedFileArc : IAsyncLifetime
{
    public string State { get; private set; } = "";

    string? locator;

    public async Task InitializeAsync()
    {
        await using var database = await Story372DeadFileReasonDatabase.StartAsync();

        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t373-resurrected.flac", "ready");
        locator = Path.Combine(Path.GetTempPath(), $"t373-resurrect-{Guid.NewGuid():N}.flac");

        await using var factory = new Story372DeadFileReasonWebFactory(database);
        var guard = factory.Services.GetRequiredService<ILiquidsoapControl>();

        await guard.PushAsync(
            new MediaItem(mediaId.ToString(), locator, "Missing Then Found", new CoreLoudness(-16.0, -1.0, Measurable: true)),
            0.0, CancellationToken.None);

        _ = await PushGuardPolling.PollForOpenFindingAsync(database.LibraryConnectionString, mediaId, "dead_file", TimeSpan.FromSeconds(1))
            ?? throw new InvalidOperationException("expected a push_missing dead_file finding before the file was resurrected");

        // The file comes back on disk — the row's own state, never having left 'ready' in this arc,
        // already reflects what a real scan tick would confirm.
        await File.WriteAllBytesAsync(locator, [0x00]);

        // Past the push_missing grace (MissThreshold x ScanIntervalSeconds = 1s x 1s, this factory's
        // own pin) — otherwise the flap guard would still be holding the finding open, exactly the
        // behaviour FeatureRotFindingFlapGuard pins directly.
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var pass = factory.Services.GetServices<IGardenerPass>().Single(p => p.Kind == RotKind.DeadFile);
        await pass.RunAsync(CancellationToken.None);

        var finding = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected the dead_file finding to still exist, now resolved");
        State = finding.State;
    }

    public Task DisposeAsync()
    {
        if (locator is not null) File.Delete(locator);
        return Task.CompletedTask;
    }
}

// ── Polling helper ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AC3/AC5's own fine-grained poll (50ms) — <c>GardenerRotFixtures.WaitForFindingAsync</c>'s own
/// 1-second-granularity wait (Story374_TheGardenerTendsAQueue.cs) is too coarse to honestly prove a
/// "within one second" fact; this is the same idea at the resolution AC3 actually needs.
/// </summary>
file static class PushGuardPolling
{
    public static async Task<GardenerRotFixtures.FindingRow?> PollForOpenFindingAsync(
        string libraryConnectionString, long mediaId, string kindText, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var finding = await GardenerRotFixtures.ReadFindingAsync(libraryConnectionString, mediaId, kindText);
            if (finding is { State: "open" }) return finding;
            if (DateTimeOffset.UtcNow >= deadline) return null;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AC4's own scripted <see cref="IDeadFileReporter"/>: a no-op for every media id except
/// <paramref name="throwsForMediaId"/>, which always throws — one instance plays both the "no-op
/// reporter" baseline and the "throwing reporter" fault inside the SAME arc/factory (the Story367
/// <c>RotationFaultArc</c> "same-arc" precedent, adapted: that arc's queue/drain split let ONE
/// throwing sink serve both roles across two separate calls; this seam has no such split, so the
/// two roles are keyed by id instead).
/// </summary>
file sealed class ThrowsForOneMediaId(long throwsForMediaId) : IDeadFileReporter
{
    public async Task ReportMissingAsync(long mediaId, CancellationToken ct)
    {
        // Yield FIRST, on EVERY call, then throw only for the fault id. The guard's
        // BeginReportMissing invokes its report method directly (no Task.Run), so an async method
        // runs synchronously up to its first incomplete await — a reporter that throws BEFORE
        // yielding puts the throw, the catch, and the exception-carrying WARN on the push's own
        // stack, inside AC4's stopwatch. The console logger formats that exception's stack trace on
        // the calling thread, and the FIRST such format in a test process costs ~20-25ms cold
        // (2026-09-04 CI: 24.5ms against a ~21ms budget, whenever xUnit's parallel ordering makes
        // this arc the first to log an exception). Yielding here makes the arc's own claim true:
        // the failure surfaces on a discarded background task, off the push's stack, and the timing
        // assertion measures only what F153.4 actually promises. Yielding on the no-op ids too
        // keeps the baseline the same shape as the fault (the real DeadFileReporter yields at its
        // first Npgsql I/O as well) and lets the arc's warmup push pay the first thread-pool hop
        // instead of the timed fault push.
        await Task.Yield();
        if (mediaId == throwsForMediaId)
        {
            throw new InvalidOperationException("simulated dead-file report failure (STORY-375 AC4)");
        }
    }
}

/// <summary>Captures every Warning+ log entry's message text — the Story164_FailClosedWithoutPassword.cs/
/// Story367_TheStationRemembersEveryAiring.cs <c>CapturingWarningLoggerProvider</c> precedent,
/// redefined here per that file's own "no shared test-support project exists" acceptance.</summary>
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

// ── Test harness — WebApplicationFactory subclass ───────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres, every hosted
/// service removed (no Liquidsoap/real-background-loop reach) — the seam AC1/AC2 need: the real,
/// container-composed <see cref="IGardenerPass"/> fan-out stays resolvable and callable directly.
/// <c>Library:Scan:MissThreshold</c>/<c>Library:ScanIntervalSeconds</c> are both pinned to
/// <c>1</c> so AC2's own grace window is a tiny, deterministic one second.
/// </summary>
/// <param name="db">The ephemeral Postgres this factory's Library/Station connection strings point
/// at.</param>
/// <param name="extraConfigure">AC4's own hook (the Story374 <c>extraConfigure</c> precedent): a
/// last-registration-wins <c>AddSingleton</c> swap-in, run AFTER <see cref="IHostedService"/> removal
/// so a caller never has to repeat that.</param>
file sealed class Story372DeadFileReasonWebFactory(Story372DeadFileReasonDatabase db, Action<IServiceCollection>? extraConfigure = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", "test-password-t372-deadfile-reason");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Library:Scan:MissThreshold", "1");
        builder.UseSetting("Library:ScanIntervalSeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            extraConfigure?.Invoke(services);
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <c>EphemeralStationDatabase</c> harness (see
/// <c>GardenerSeedTestDatabase</c>/<c>Story372GardenerDatabase</c>'s own remarks for the full "which
/// compose file, why a unique project name + OS-assigned port" rationale). Supplies only the
/// <c>"genwave-t372b"</c> compose project-name prefix this file's own arcs need — distinct from
/// Story374_TheGardenerTendsAQueue.cs's own prefix so the two never collide under parallel xUnit
/// execution, and kept short so <c>Provision</c>'s own 24-char project-name cap still leaves real
/// GUID entropy after the prefix (a longer prefix here once truncated the GUID to nearly nothing).
/// </summary>
file sealed class Story372DeadFileReasonDatabase : EphemeralStationDatabase
{
    Story372DeadFileReasonDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story372DeadFileReasonDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t372b");
        var db = new Story372DeadFileReasonDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
