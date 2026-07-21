// STORY-144 — Missing release years filled from MusicBrainz (Epic X / SPEC F48.3–F48.6,
// closes gitea-#208) — claim/pacing pipeline half. The client half lives in
// Specs/Story144_MusicBrainzYearLookup.cs (this project); the settings keys in
// Host.Tests/Specs/Story144_YearLookupSettingsKeys.cs.
//
// BDD specification — xUnit (real Postgres via the DatabaseFixture; the lookup itself is a fake
// IYearLookup — F48.7). Implemented X5 (2026-07-14).

using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureYearLookupPipeline
{
    static readonly LibraryScope Scope = new([1L]);

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Inline DTO for querying year-lookup-relevant columns directly from Postgres — mirrors the
    // BpmRow helper in Story142_BpmEnrichmentAndBackfill.
    sealed class YearRow
    {
        public string? State { get; set; }
        public int? Year { get; set; }
        public DateTime? YearLookupAt { get; set; }
        public DateTime? YearLookupMissedAt { get; set; }
    }

    static async Task<YearRow> SelectYearRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<YearRow>(
            "select state, year, year_lookup_at, year_lookup_missed_at from library.media where id = @id", new { id });
    }

    /// <summary>
    /// Seeds a fresh <c>ready</c> row via the real enrichment write path (never raw SQL) with the
    /// given tag values and year. A unique synthetic path is generated each call — <c>InsertDiscoveredAsync</c>
    /// never opens the file, so no real media/ffmpeg is needed for this pipeline's tests at all.
    /// </summary>
    static async Task<long> SeedRowAsync(MediaRepository repo, string? artist, string? title, int? year = null)
    {
        var path = $"/synthetic/{Guid.NewGuid():N}.flac";
        var id = await repo.InsertDiscoveredAsync(path, "flac", 100, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(
            id, Harness.ReadyResultWith(artist: artist, title: title) with { Year = year },
            CancellationToken.None);
        return id;
    }

    // ---------------------------------------------------------------------
    // CLAIM LOOP — predicate + write shape (F48.3/F48.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheClaimLoopFillsMissingYears(DatabaseFixture db)
    {
        [Fact]
        public async Task OnlyYearlessUnattemptedTaggedRowsAreClaimed()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Eligible: year null, year_lookup_missed_at null, artist/title non-blank.
            var eligible = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");
            // Already has a year — never claimed (F48.4).
            var hasYear = await SeedRowAsync(repo, artist: "Someone Else", title: "Another Song", year: 1999);
            // Already attempted and MISSED (sentinel stamped) — not reclaimed (F48.3, F76.2).
            var alreadyAttempted = await SeedRowAsync(repo, artist: "Been Tried", title: "No Match");
            await repo.WriteYearLookupResultAsync(alreadyAttempted, null, callFailed: false, CancellationToken.None);
            // Blank artist — nothing to search MusicBrainz with, never claimed.
            var blankArtist = await SeedRowAsync(repo, artist: "  ", title: "Untitled Instrumental");

            var fake = new FakeYearLookup();
            fake.SetFallback(1975);
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Equal(1, fake.Calls);
            // Album comes along for the ride from Harness.ReadyResultWith's fixed "al" default —
            // this fact's concern is which ROW got claimed, not the album value itself.
            var call = fake.CallArgs.Single();
            Assert.Equal("The Testers", call.Artist);
            Assert.Equal("Testing Waters", call.Title);

            Assert.NotNull((await SelectYearRowAsync(db, eligible)).YearLookupAt);
            Assert.Null((await SelectYearRowAsync(db, hasYear)).YearLookupAt);
            Assert.Equal(1999, (await SelectYearRowAsync(db, hasYear)).Year);
        }

        [Fact]
        public async Task AConfidentResultWritesTheYearAndStampsTheSentinel()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fake = new FakeYearLookup();
            fake.Enqueue(1975);
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            var row = await SelectYearRowAsync(db, id);
            Assert.Equal(1975, row.Year);
            Assert.NotNull(row.YearLookupAt);
        }

        [Fact]
        public async Task ALookupWriteNeverStampsTagsEditedAt()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");
            Assert.Null(await Harness.TagsEditedAtOfAsync(db, id));

            var fake = new FakeYearLookup();
            fake.Enqueue(1975);
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            // The lookup is enrichment, never an operator edit (F48.4) — tags_edited_at is
            // untouched, so file tags still win a future re-scan.
            Assert.Null(await Harness.TagsEditedAtOfAsync(db, id));
        }
    }

    // ---------------------------------------------------------------------
    // PACING — sequential, one in flight (F48.3)
    //
    // The >= 1s inter-request SPACING itself moved off this loop entirely (SPEC F76.1, STORY-200):
    // it used to be a Task.Delay hand-rolled right here, pacing only this one caller. It is now
    // MusicBrainzRateLimiter — a shared, process-wide gate MusicBrainzYearLookup awaits immediately
    // before every HTTP call, so the etiquette rule holds no matter what drives the client. That
    // pacing is pinned deterministically (a fake clock, no real sleep) in
    // Specs/Story200_MusicBrainzEtiquette.cs; this loop's own remaining contract is simply that it
    // never overlaps two lookups at once.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAttemptsArePacedAndSingleFile(DatabaseFixture db)
    {
        [Fact]
        public async Task BatchRowsProcessSequentiallyNeverConcurrently()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            await SeedRowAsync(repo, artist: "Artist One", title: "Song One");
            await SeedRowAsync(repo, artist: "Artist Two", title: "Song Two");

            var fake = new FakeYearLookup();
            fake.SetFallback(null);
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Equal(2, fake.Calls);
            Assert.Equal(1, fake.MaxObservedConcurrency);
        }
    }

    // ---------------------------------------------------------------------
    // KILL SWITCH — Enabled read live via IOptionsMonitor (F48.5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheKillSwitchIsLive(DatabaseFixture db)
    {
        [Fact]
        public async Task DisablingTheLookupStopsClaimsOnTheNextTick()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fake = new FakeYearLookup();
            fake.SetFallback(1975);
            var options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions { Enabled = false });
            var svc = Harness.BackfillYearLookupWith(repo, fake, options);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            // Disabled: no claim query at all — the row is untouched, not even the sentinel.
            Assert.Equal(0, fake.Calls);
            Assert.Null((await SelectYearRowAsync(db, id)).YearLookupAt);

            // The live edit: no re-construction — same service instance, new value takes effect
            // on the very next tick, no api restart.
            options.CurrentValue = new YearLookupOptions { Enabled = true };
            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Equal(1, fake.Calls);
            Assert.NotNull((await SelectYearRowAsync(db, id)).YearLookupAt);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFailuresStampAndNeverBlock(DatabaseFixture db)
    {
        [Fact]
        public async Task ALowConfidenceAttemptStampsTheSentinelAndWritesNoYear()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fake = new FakeYearLookup();
            fake.Enqueue(null, failed: false);   // a legal "no confident match" — not a failure
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            var row = await SelectYearRowAsync(db, id);
            Assert.Null(row.Year);
            Assert.NotNull(row.YearLookupAt);
            // A genuine miss stamps the F76.2 re-claim gate too — this row is excluded going forward.
            Assert.NotNull(row.YearLookupMissedAt);
        }

        [Fact]
        public async Task AFailedAttemptStampsTheAttemptedAtTelemetryOnly()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fake = new FakeYearLookup();
            fake.Enqueue(null, failed: true);   // endpoint outage
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            var row = await SelectYearRowAsync(db, id);
            Assert.Null(row.Year);
            Assert.NotNull(row.YearLookupAt);
            // Unlike a genuine miss, a failed round trip never stamps the F76.2 re-claim gate — this
            // is what leaves the row eligible for the very next pass (SPEC F76.2).
            Assert.Null(row.YearLookupMissedAt);
        }

        [Fact]
        public async Task AFailedAttemptLeavesTheRowEligibleSoTheNextPassRetriesIt()
        {
            // SPEC F76.2 (STORY-200) supersedes the old F48.3 assumption that ANY attempt — including
            // an endpoint failure — permanently excludes a row: an outage is transient, so a row that
            // merely failed to complete a round trip must be retried on the very next pass, never
            // stamped away like a genuine miss.
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fake = new FakeYearLookup();
            fake.SetFallback(null, failed: true);   // every attempt this tick fails
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);
            Assert.Equal(1, fake.Calls);

            // Run again — the row was never miss-stamped (it merely failed), so it is reclaimed and
            // MusicBrainz is asked again; calls increase.
            await svc.BackfillYearLookupAsync(CancellationToken.None);
            Assert.Equal(2, fake.Calls);
        }

        [Fact]
        public async Task AnEndpointOutageWarnsOncePerTickNotPerRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            await SeedRowAsync(repo, artist: "Artist One", title: "Song One");
            await SeedRowAsync(repo, artist: "Artist Two", title: "Song Two");
            await SeedRowAsync(repo, artist: "Artist Three", title: "Song Three");

            var fake = new FakeYearLookup();
            fake.SetFallback(null, failed: true);   // every attempt this tick fails
            var capturingLogger = new CapturingLogger<EnrichmentService>();
            var svc = new EnrichmentService(
                repo,
                new Enricher(
                    new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(),
                    NullLogger<Enricher>.Instance),
                System.Threading.Channels.Channel.CreateUnbounded<long>(),
                new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
                capturingLogger,
                new FakeCueAnalyzer(),
                Microsoft.Extensions.Options.Options.Create(new GenWave.Loudness.CueDetectionOptions()),
                new FakeEnergyAnalyzer(),
                new FakeBpmAnalyzer(),
                fake,
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Equal(3, fake.Calls);
            Assert.Single(capturingLogger.Warnings);
        }

        [Fact]
        public async Task ARowWithAYearIsNeverClaimed()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters", year: 2001);

            var fake = new FakeYearLookup();
            fake.SetFallback(1975);
            var svc = Harness.BackfillYearLookupWith(repo, fake);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Equal(0, fake.Calls);
            var row = await SelectYearRowAsync(db, id);
            Assert.Equal(2001, row.Year);
            Assert.Null(row.YearLookupAt);
        }

        [Fact]
        public async Task CueEnergyAndBpmBackfillsProceedWhileTheLookupFails()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // A row pre-dating cue/energy/bpm analysis (all sentinels null) — the pre-existing
            // three backfills' own claim predicates.
            var multiPath = $"/synthetic/{Guid.NewGuid():N}.flac";
            var multiId = await repo.InsertDiscoveredAsync(multiPath, "flac", 100, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(
                multiId,
                Harness.ReadyResult(true) with
                {
                    CueAnalyzedAt = null, EnergyAnalyzedAt = null, BpmAnalyzedAt = null,
                },
                CancellationToken.None);

            // A year-eligible row whose lookup will fail this tick.
            var yearId = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            var fakeCue = new FakeCueAnalyzer();
            fakeCue.Returns(new CuePoints(0.5, 9.5));
            var fakeEnergy = new FakeEnergyAnalyzer();
            fakeEnergy.Returns(new EnergyPoints(0.4, 0.6));
            var fakeBpm = new FakeBpmAnalyzer();
            fakeBpm.Returns(120.0);
            var fakeYear = new FakeYearLookup();
            fakeYear.SetFallback(null, failed: true);

            var svc = new EnrichmentService(
                repo,
                new Enricher(
                    new FakeLoudnessAnalyzer(), fakeCue, fakeEnergy, fakeBpm, NullLogger<Enricher>.Instance),
                System.Threading.Channels.Channel.CreateUnbounded<long>(),
                new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
                NullLogger<EnrichmentService>.Instance,
                fakeCue,
                Microsoft.Extensions.Options.Options.Create(new GenWave.Loudness.CueDetectionOptions()),
                fakeEnergy,
                fakeBpm,
                fakeYear,
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

            // Mirrors RunBackfillLoopAsync's own call order within one tick.
            await svc.BackfillCueAsync(CancellationToken.None);
            await svc.BackfillEnergyAsync(CancellationToken.None);
            await svc.BackfillBpmAsync(CancellationToken.None);
            await svc.BackfillYearLookupAsync(CancellationToken.None);

            var (cueInSec, cueOutSec, introEnergy, outroEnergy) = await MeasurementsOfMultiRowAsync(db, multiId);
            Assert.Equal(0.5, cueInSec);
            Assert.Equal(9.5, cueOutSec);
            Assert.Equal(0.4, introEnergy);
            Assert.Equal(0.6, outroEnergy);

            var yearRow = await SelectYearRowAsync(db, yearId);
            Assert.NotNull(yearRow.YearLookupAt);   // attempted despite the endpoint failure
            Assert.Null(yearRow.Year);
        }

        static async Task<(double? CueInSec, double? CueOutSec, double? IntroEnergy, double? OutroEnergy)>
            MeasurementsOfMultiRowAsync(DatabaseFixture f, long id)
        {
            await using var conn = await f.DataSource.OpenConnectionAsync();
            return await conn.QuerySingleAsync<(double?, double?, double?, double?)>(
                "select cue_in_sec, cue_out_sec, intro_energy, outro_energy from library.media where id = @id",
                new { id });
        }
    }

    // ---------------------------------------------------------------------
    // REENRICH — the "year" token nulls the sentinel ONLY (F48.6)
    //
    // The Core-level half of this contract (ReenrichFields.Year is a legal, distinct bit flag) is
    // pinned in Core.Tests/Specs/Story144_YearLookupContract.cs — Core.Tests cannot see the SQL
    // arm (MediaRepository.BuildReenrichSetClauses), so that behavior is pinned here instead.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReenrichResetsOnlyTheSentinel(DatabaseFixture db)
    {
        [Fact]
        public async Task AYearReenrichResetNullsOnlyTheSentinelValueUntouched()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters", year: 1975);
            await repo.WriteYearLookupResultAsync(id, 1975, callFailed: false, CancellationToken.None);

            var before = await SelectYearRowAsync(db, id);
            Assert.Equal(1975, before.Year);
            Assert.NotNull(before.YearLookupAt);

            var result = await ((IAdminMediaReenrichment)repo).ScheduleAsync(
                id.ToString(), ReenrichFields.Year, Scope, CancellationToken.None);
            Assert.Equal(ReenrichResult.Scheduled, result);

            // Sentinel-only reset (SPEC F48.6): year_lookup_at nulls, year is UNTOUCHED, state
            // unchanged — unlike every sibling flag.
            var after = await SelectYearRowAsync(db, id);
            Assert.Equal(1975, after.Year);
            Assert.Null(after.YearLookupAt);
            Assert.Equal("ready", after.State);
        }
    }
}
