// STORY-341 — Loudness-first fast pass (F135)
//
// BDD specification — xUnit. Integration: real Postgres via DatabaseCollection + real
// ffmpeg (the suite's standing tools).
//
// The contract under spec: first-pass enrichment slims to TagLib (tags + duration) +
// loudness only → the existing atomic write flips state='ready' with cue/energy/BPM
// NULL; the second-tier backfill lanes sweep the rest; the failure contract and the
// engine-facing row shape (F135.2) are unchanged.

using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLoudnessFirstFastPass
{
    // Inline DTO for querying every fast-pass-relevant column directly from Postgres.
    sealed class FastPassRow
    {
        public string? State { get; set; }
        public int? DurationMs { get; set; }
        public double? IntegratedLufs { get; set; }
        public double? CueInSec { get; set; }
        public double? CueOutSec { get; set; }
        public DateTime? CueAnalyzedAt { get; set; }
        public double? IntroEnergy { get; set; }
        public double? OutroEnergy { get; set; }
        public DateTime? EnergyAnalyzedAt { get; set; }
        public double? Bpm { get; set; }
        public DateTime? BpmAnalyzedAt { get; set; }
    }

    static async Task<FastPassRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<FastPassRow>(
            "select state, duration_ms, integrated_lufs, cue_in_sec, cue_out_sec, cue_analyzed_at, " +
            "intro_energy, outro_energy, energy_analyzed_at, bpm, bpm_analyzed_at " +
            "from library.media where id = @id",
            new { id });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the slim first pass (AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFastPassMakesARowReadyWithLoudnessOnly(DatabaseFixture db)
    {
        // Arrange: insert a discovered row for a real generated WAV; run first-pass
        // enrichment (Enricher.EnrichAsync via the service path).
        async Task<FastPassRow> ArrangeAndActAsync(string fileName)
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, fileName,
                    title: "Fast Pass Song", artist: "Fast Pass Artist", album: "Fast Pass Album",
                    genre: "Electronic", year: 2026);
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                return await SelectRowAsync(db, id);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RowReachesStateReady()
        {
            var row = await ArrangeAndActAsync("fastpass_ready.flac");
            Assert.Equal("ready", row.State);
        }

        [Fact]
        public async Task IntegratedLufsIsMeasured()
        {
            var row = await ArrangeAndActAsync("fastpass_lufs.flac");
            Assert.NotNull(row.IntegratedLufs);
        }

        [Fact]
        public async Task DurationMsIsSetFromTagRead()
        {
            var row = await ArrangeAndActAsync("fastpass_duration.flac");
            Assert.True(row.DurationMs is > 1500 and < 2500);   // TestMedia.CreateTone's default ~2s
        }

        [Fact]
        public async Task CueColumnsRemainNull()
        {
            // cue_in_sec, cue_out_sec, cue_analyzed_at all NULL after the fast pass —
            // the backfill predicate (state='ready' AND cue_analyzed_at IS NULL) finds it.
            var row = await ArrangeAndActAsync("fastpass_cue_null.flac");
            Assert.Null(row.CueInSec);
            Assert.Null(row.CueOutSec);
            Assert.Null(row.CueAnalyzedAt);
        }

        [Fact]
        public async Task EnergyColumnsRemainNull()
        {
            var row = await ArrangeAndActAsync("fastpass_energy_null.flac");
            Assert.Null(row.IntroEnergy);
            Assert.Null(row.OutroEnergy);
            Assert.Null(row.EnergyAnalyzedAt);
        }

        [Fact]
        public async Task BpmRemainsNull()
        {
            var row = await ArrangeAndActAsync("fastpass_bpm_null.flac");
            Assert.Null(row.Bpm);
            Assert.Null(row.BpmAnalyzedAt);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the backfill sweeps the rest (AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBackfillLanesSweepFastPassRows(DatabaseFixture db)
    {
        // Arrange: a fast-pass-ready row; run the second-tier backfill lanes
        // (cue → energy → bpm) via the existing EnrichmentService paths.
        async Task<long> SeedFastPassRowAsync(string fileName)
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, fileName);
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);
                return id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueBackfillFillsCueColumns()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedFastPassRowAsync("backfill_sweep_cue.flac");

            var fakeCue = new FakeCueAnalyzer();
            fakeCue.Returns(new CuePoints(0.5, 9.5));
            await Harness.BackfillWith(repo, fakeCue).BackfillCueAsync(CancellationToken.None);

            var row = await SelectRowAsync(db, id);
            Assert.Equal(0.5, row.CueInSec);
        }

        [Fact]
        public async Task EnergyBackfillFillsIntroAndOutroEnergy()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedFastPassRowAsync("backfill_sweep_energy.flac");

            // The energy claim gates on cue_analyzed_at IS NOT NULL (SPEC F135.5) — the cue lane
            // runs first, exactly as RunBackfillLoopAsync orders its own tick.
            var fakeCue = new FakeCueAnalyzer();
            fakeCue.Returns(new CuePoints(0.5, 9.5));
            var fakeEnergy = new FakeEnergyAnalyzer();
            fakeEnergy.Returns(new EnergyPoints(0.6, 0.4));
            var svc = Harness.BackfillLanesWith(repo, fakeCue, fakeEnergy, new FakeBpmAnalyzer(), batchSize: 50);
            await svc.BackfillCueAsync(CancellationToken.None);
            await svc.BackfillEnergyAsync(CancellationToken.None);

            var row = await SelectRowAsync(db, id);
            Assert.Equal(0.6, row.IntroEnergy);
        }

        [Fact]
        public async Task BpmBackfillFillsBpm()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedFastPassRowAsync("backfill_sweep_bpm.flac");

            // The bpm claim gates on cue_analyzed_at IS NOT NULL (SPEC F135.5) — the cue lane runs
            // first, exactly as RunBackfillLoopAsync orders its own tick.
            var fakeCue = new FakeCueAnalyzer();
            fakeCue.Returns(new CuePoints(0.5, 9.5));
            var fakeBpm = new FakeBpmAnalyzer();
            fakeBpm.Returns(128.0);
            var svc = Harness.BackfillLanesWith(repo, fakeCue, new FakeEnergyAnalyzer(), fakeBpm, batchSize: 50);
            await svc.BackfillCueAsync(CancellationToken.None);
            await svc.BackfillBpmAsync(CancellationToken.None);

            var row = await SelectRowAsync(db, id);
            Assert.Equal(128.0, row.Bpm);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — lane ordering is a claim-level contract (SPEC F135.5, T314 review fix): the
    // energy and BPM claim queries gate on cue_analyzed_at IS NOT NULL, so a row's energy/BPM is
    // never measured over an un-trimmed file even when a backfill batch spans multiple ticks.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyAndBpmClaimsGateOnCueHavingRun(DatabaseFixture db)
    {
        // Deliberately > BatchSize: one cue tick claims only BatchSize of these rows, leaving the
        // rest behind cue_analyzed_at IS NULL — exactly the multi-tick-drain overlap window the
        // T314 review measured on real PG (energy/bpm claiming rows the cue lane hadn't reached).
        const int BatchSize = 3;
        const int TotalRows = 7;

        async Task<List<long>> SeedFastPassRowsAsync(MediaRepository repo)
        {
            var ids = new List<long>();
            for (var i = 0; i < TotalRows; i++)
            {
                // Synthetic, never-opened paths — the claim gate is proven by the SQL predicate and
                // the fakes below, not by real audio (mirrors the F135.4 fairness spec's own idiom).
                var syntheticPath = $"/synthetic/{Guid.NewGuid():N}.flac";
                var id = await repo.InsertDiscoveredAsync(syntheticPath, "flac", 100, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(
                    id,
                    // Fast-pass-shaped (SPEC F135.1): cue/energy/bpm and their sentinels all NULL.
                    Harness.ReadyResult(true) with { CueAnalyzedAt = null, EnergyAnalyzedAt = null, BpmAnalyzedAt = null },
                    CancellationToken.None);
                ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// Seeds TotalRows fast-pass rows, drives one cue tick then one <paramref name="secondLaneTick"/>
        /// tick against the SAME service instance (one drain, two lanes, matching RunBackfillLoopAsync's
        /// own cue-then-energy-then-bpm ordering within a single iteration), and returns every seeded
        /// row's post-tick state.
        /// </summary>
        async Task<List<FastPassRow>> DriveCueThenAsync(
            Func<EnrichmentService, Task> secondLaneTick, IEnergyAnalyzer? energy = null, IBpmAnalyzer? bpm = null)
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ids = await SeedFastPassRowsAsync(repo);

            var fakeCue = new FakeCueAnalyzer();
            fakeCue.Returns(new CuePoints(0.5, 9.5));
            var svc = Harness.BackfillLanesWith(
                repo, fakeCue, energy ?? new FakeEnergyAnalyzer(), bpm ?? new FakeBpmAnalyzer(), BatchSize);

            await svc.BackfillCueAsync(CancellationToken.None);   // tick 1 of the cue lane — BatchSize of TotalRows
            await secondLaneTick(svc);                             // tick 1 of the second lane, same instance

            var rows = new List<FastPassRow>();
            foreach (var id in ids)
                rows.Add(await SelectRowAsync(db, id));
            return rows;
        }

        [Fact]
        public async Task EveryEnergyStampedRowAlsoHasCueStamped()
        {
            var fakeEnergy = new FakeEnergyAnalyzer();
            fakeEnergy.Returns(new EnergyPoints(0.6, 0.4));
            var rows = await DriveCueThenAsync(
                svc => svc.BackfillEnergyAsync(CancellationToken.None), energy: fakeEnergy);

            Assert.All(rows.Where(r => r.EnergyAnalyzedAt is not null), row => Assert.NotNull(row.CueAnalyzedAt));
        }

        [Fact]
        public async Task EveryBpmStampedRowAlsoHasCueStamped()
        {
            var fakeBpm = new FakeBpmAnalyzer();
            fakeBpm.Returns(128.0);
            var rows = await DriveCueThenAsync(
                svc => svc.BackfillBpmAsync(CancellationToken.None), bpm: fakeBpm);

            Assert.All(rows.Where(r => r.BpmAnalyzedAt is not null), row => Assert.NotNull(row.CueAnalyzedAt));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — cue's sentinel stamps even when its analyzer throws, so no row can strand
    // behind the F135.5 gate forever (the review's proven no-strand invariant)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioACueAnalyzerThrowStillStampsTheSentinel(DatabaseFixture db)
    {
        async Task<long> SeedFastPassRowAsync(MediaRepository repo, string fileName)
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, fileName);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);
                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);
                return id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task TheThrowingRowIsStampedAnywayNotLeftClaimableForever()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedFastPassRowAsync(repo, "cue_throw_stamps.flac");

            var throwingCue = new FakeCueAnalyzer();
            throwingCue.Throws(new InvalidOperationException("simulated cue analysis failure"));
            var svc = Harness.BackfillLanesWith(
                repo, throwingCue, new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), batchSize: 50);

            await svc.BackfillCueAsync(CancellationToken.None);

            var row = await SelectRowAsync(db, id);
            Assert.NotNull(row.CueAnalyzedAt);
        }

        [Fact]
        public async Task ThatSameRowIsClaimableByEnergyOnALaterTick()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedFastPassRowAsync(repo, "cue_throw_then_energy_claims.flac");

            var throwingCue = new FakeCueAnalyzer();
            throwingCue.Throws(new InvalidOperationException("simulated cue analysis failure"));
            var fakeEnergy = new FakeEnergyAnalyzer();
            fakeEnergy.Returns(new EnergyPoints(0.6, 0.4));
            var svc = Harness.BackfillLanesWith(repo, throwingCue, fakeEnergy, new FakeBpmAnalyzer(), batchSize: 50);

            await svc.BackfillCueAsync(CancellationToken.None);     // tick 1: cue throws, sentinel stamps anyway
            await svc.BackfillEnergyAsync(CancellationToken.None);  // tick 2 (a later tick): now claimable

            var row = await SelectRowAsync(db, id);
            Assert.NotNull(row.EnergyAnalyzedAt);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — air is safe the whole time (AC3: the row shape F135.2 leans on)
    // ---------------------------------------------------------------------

    [Trait("Category", "Unit")]
    public sealed class ScenarioAFastPassRowAnnotatesSafely
    {
        // Arrange: a MediaRow shaped exactly like a fast-pass product — real LUFS,
        // measurable, cue/energy NULL.
        static MediaRow FastPassShapedRow() => new()
        {
            Id = 1,
            Path = "/media/fastpass.flac",
            State = "ready",
            IntegratedLufs = -14.0,
            TruePeakDbtp = -1.0,
            Measurable = true,
            CueInSec = null,
            CueOutSec = null,
            IntroEnergy = null,
            OutroEnergy = null,
        };

        [Fact]
        public void ResolveCueReturnsNullSoCueKeysAreOmitted()
        {
            var row = FastPassShapedRow();
            Assert.Null(row.ResolveCue(NullLogger<MediaRow>.Instance));
        }

        [Fact]
        public void ResolveEnergyReturnsNullsSoTheFixedCrossfadeApplies()
        {
            var row = FastPassShapedRow();
            Assert.Equal((null, null), row.ResolveEnergy(NullLogger<MediaRow>.Instance));
        }

        [Fact]
        public void ToReferenceCarriesTheMeasuredLoudness()
        {
            // replay_gain is real from the first airing — loudness matching never sacrificed.
            var row = FastPassShapedRow();
            var reference = row.ToReference(NullLogger<MediaRow>.Instance);
            Assert.Equal(new LoudnessMeasurement(-14.0, -1.0, true), reference.Loudness);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — fairness on a big drop (AC4 / F135.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDiscoveryKeepsPriorityOverBackfill(DatabaseFixture db)
    {
        /// <summary>
        /// A cue analyzer with an artificial per-call delay — mirrors FakeYearLookup's own
        /// Task.Delay idiom (that fake lives in Fakes/, this one is scoped to this one spec since
        /// no other test needs a slow cue double). Makes the backfill lane's wall-clock cost
        /// observable/deterministic instead of racing a fast fake to completion.
        /// </summary>
        sealed class SlowCueAnalyzer(TimeSpan delay) : ICueAnalyzer
        {
            public async Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct)
            {
                await Task.Delay(delay, ct);
                return new CuePoints(0.5, 9.5);
            }
        }

        static async Task WaitUntilReadyAsync(DatabaseFixture db, long id, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (await Harness.StateOfAsync(db, id) != "ready")
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Row did not reach 'ready' within the timeout.");
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task NewDiscoveredRowsReachReadyBeforeBackfillDrainsItsQueue()
        {
            // ONE EnrichmentService instance drives both lanes here (the T314 review's fix for the
            // earlier two-instance version, which could not contend by construction): the real
            // enrichQueue + worker pool (ReconcileWorkerPool/WorkerAsync) drain discovery exactly as
            // production does, while BackfillCueAsync drains the deep backfill queue on the SAME
            // instance — so any observed ordering is this one service's own architecture, not two
            // unrelated tasks racing.
            await db.ResetAsync();
            var enrichQueue = Channel.CreateUnbounded<long>();
            var repo = Harness.Repo(db, enrichQueue);

            // A deep backfill queue: many fast-pass-ready rows still needing cue analysis.
            // Synthetic, never-opened paths — only the SQL claim and the (fake) analyzer matter.
            const int backfillDepth = 40;
            for (var i = 0; i < backfillDepth; i++)
            {
                var syntheticPath = $"/synthetic/{Guid.NewGuid():N}.flac";
                var id = await repo.InsertDiscoveredAsync(syntheticPath, "flac", 100, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true) with { CueAnalyzedAt = null }, CancellationToken.None);
            }

            // A freshly discovered file, competing alongside the deep backfill queue.
            var dir = TestMedia.NewTempDir();
            try
            {
                var freshPath = TestMedia.CreateTone(dir, "fairness_fresh.flac");
                var freshId = await repo.InsertDiscoveredAsync(
                    freshPath, "flac", new FileInfo(freshPath).Length, Harness.Mtime, CancellationToken.None);

                var slowCue = new SlowCueAnalyzer(TimeSpan.FromMilliseconds(40));
                var svc = new EnrichmentService(
                    repo,
                    // A fake, instant loudness analyzer isolates this from ffmpeg's own wall-clock
                    // cost, so the ordering this proves is architectural (independent execution paths
                    // within one instance), not a timing accident.
                    new Enricher(new FakeLoudnessAnalyzer()),
                    enrichQueue,
                    new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
                    NullLogger<EnrichmentService>.Instance,
                    slowCue,
                    Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions { BackfillBatchSize = backfillDepth }),
                    new FakeEnergyAnalyzer(),
                    new FakeBpmAnalyzer(),
                    new FakeYearLookup(),
                    new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

                // Spin up the real worker pool draining enrichQueue (no BackgroundService host needed
                // — ReconcileWorkerPool spawns its own worker tasks, mirroring Story156's own idiom).
                svc.ReconcileWorkerPool(CancellationToken.None);

                // Kick off the deep backfill lane WITHOUT awaiting it yet.
                var backfillTask = svc.BackfillCueAsync(CancellationToken.None);

                // Concurrently hand the freshly discovered row to this SAME instance's real queue —
                // the worker pool claims and enriches it exactly as production discovery would.
                await enrichQueue.Writer.WriteAsync(freshId, CancellationToken.None);
                await WaitUntilReadyAsync(db, freshId, TimeSpan.FromSeconds(5));

                // The fast pass reached ready while the backfill lane — 40 rows * 40ms — is still draining.
                Assert.False(backfillTask.IsCompleted);

                await backfillTask;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — loudness failure still fails (AC5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLoudnessFailureStillMarksFailed(DatabaseFixture db)
    {
        [Fact]
        public async Task ARowWhoseLoudnessAnalysisThrowsGoesStateFailed()
        {
            // A non-audio file behind an audio extension: fast pass runs, loudness
            // throws, MarkFailedAsync fires — the failure contract is untouched.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateCorrupt(dir, "fastpass_corrupt.mp3");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("failed", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
