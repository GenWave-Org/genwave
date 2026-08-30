// STORY-375 — Dead files are visible the moment they cost a pick (SPEC F153.3–F153.4 · PLAN T372/T373)
//
// BDD specification — xUnit. AC1/AC2 WIRED at T372; AC3/AC4/AC5 stay pending T373. Entry-point
// discipline: AC1/AC2 run the real, container-composed dead_file IGardenerPass (IGardenerPass is a
// PUBLIC GenWave.Core.Abstractions port, resolvable from this test assembly with no
// InternalsVisibleTo into GenWave.MediaLibrary) against rows arranged directly in the ephemeral
// Postgres (Support/EphemeralStationDatabase, the Story345/Story366/Story367/T372 factory idiom over
// WebApplicationFactory<Program>) — state=failed, and a stale unavailable row past
// Library:Scan:MissThreshold × Library:ScanIntervalSeconds. AC3–AC5 drive the PRODUCTION feeder path:
// the real MediaExistencePushGuard wired ahead of ILiquidsoapControl inside the factory's own
// container, pushing a MediaItem whose locator points into a temp media root (Path.GetTempPath()-
// rooted, mirroring Gh612_MediaExistencePushGuard.cs) with the file absent — then present again for
// AC5's resurrection — and the real IDeadFileReporter reporting fire-and-forget into the same
// rot_finding table AC1/AC2 read. AC4's throwing reporter is a scripted IDeadFileReporter substitute
// swapped into the container via services.Replace, timed against the guard's own decline to prove the
// WARN never costs the push a millisecond.

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Support;

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

    public sealed class ScenarioAPushMissReportsImmediately
    {
        // Given a ready row whose file is missing on disk, When the feeder pushes it.
        [Fact(Skip = "pending T373 (STORY-375 AC3)")]
        public void ThePushIsDeclinedUnchanged() => Assert.Fail("pending T373");

        [Fact(Skip = "pending T373 (STORY-375 AC3)")]
        public void AFindingExistsWithinOneSecondWithReasonPushMissing() => Assert.Fail("pending T373");
    }

    public sealed class ScenarioTheReporterNeverBlocks
    {
        // Given a reporter that throws, When the feeder pushes a missing file.
        [Fact(Skip = "pending T373 (STORY-375 AC4)")]
        public void TheDeclinesTimingIsUnchanged() => Assert.Fail("pending T373");

        [Fact(Skip = "pending T373 (STORY-375 AC4)")]
        public void ExactlyOneWarnNamesTheReporter() => Assert.Fail("pending T373");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the file comes back
    // ---------------------------------------------------------------------

    public sealed class ScenarioAResurrectedFileResolvesIt
    {
        // Given a push_missing finding and the file back on disk, When the scan sights it and the pass runs.
        [Fact(Skip = "pending T373 (STORY-375 AC5)")]
        public void TheFindingIsResolved() => Assert.Fail("pending T373");
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

// ── Test harness — WebApplicationFactory subclass ───────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres, every hosted
/// service removed (no Liquidsoap/real-background-loop reach) — the seam AC1/AC2 need: the real,
/// container-composed <see cref="IGardenerPass"/> fan-out stays resolvable and callable directly.
/// <c>Library:Scan:MissThreshold</c>/<c>Library:ScanIntervalSeconds</c> are both pinned to
/// <c>1</c> so AC2's own grace window is a tiny, deterministic one second.
/// </summary>
file sealed class Story372DeadFileReasonWebFactory(Story372DeadFileReasonDatabase db) : WebApplicationFactory<Program>
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
