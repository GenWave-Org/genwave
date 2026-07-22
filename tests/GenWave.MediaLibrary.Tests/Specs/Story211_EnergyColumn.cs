// STORY-211 — Every ready track knows its energy
//
// BDD specification — xUnit (SPEC F80.1, F80.2, F80.3). PLAN T57 — the first shippable slice.
// Percentile facts run against a real Postgres (Story142 backfill idiom); the distribution
// sanity scenario uses the demo-library fixture set.

using Dapper;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnergyColumn
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Inline DTO for querying energy-relevant columns directly from Postgres — mirrors BpmRow in
    // Story142_BpmEnrichmentAndBackfill.
    sealed class EnergyRow
    {
        public long Id { get; set; }
        public double? IntegratedLufs { get; set; }
        public double? Energy { get; set; }
    }

    static async Task<EnergyRow> SelectEnergyRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<EnergyRow>(
            "select id, integrated_lufs, energy from library.media where id = @id",
            new { id });
    }

    static async Task<string> SelectXminAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<string>(
            "select xmin::text from library.media where id = @id", new { id });
    }

    /// <summary>Seeds one ready row via the real enrichment write path with the given LUFS, mirroring
    /// Story145's "InsertDiscoveredAsync + WriteEnrichmentAsync, no real file needed" idiom — this
    /// exercises the write/recompute SQL only, never a file or analyzer.</summary>
    static async Task<long> SeedReadyAsync(MediaRepository repo, string path, double lufs)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true) with { IntegratedLufs = lufs }, CancellationToken.None);
        return id;
    }

    /// <summary>An independent, hand-computed percentile-rank oracle (distinct values only — no ties
    /// in any fixture below) so tests never assert the production SQL against itself.</summary>
    static double ExpectedPercentRank(double value, IReadOnlyList<double> population)
    {
        var sorted = population.OrderBy(x => x).ToList();
        if (sorted.Count == 1) return 0.0;
        return (double)sorted.IndexOf(value) / (sorted.Count - 1);
    }

    static bool IsNonDecreasing(IReadOnlyList<double> values)
    {
        for (var i = 1; i < values.Count; i++)
            if (values[i] < values[i - 1]) return false;
        return true;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — percentile semantics (F80.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPercentileSemantics(DatabaseFixture db)
    {
        // Arrange (T57): ready library seeded with known LUFS values (distinct, unordered).
        static readonly double[] Lufs = [-8.0, -20.0, -14.0, -26.0, -11.0];

        async Task<List<long>> SeedReadyLibraryAsync()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ids = new List<long>();
            foreach (var (lufs, i) in Lufs.Select((l, i) => (l, i)))
                ids.Add(await SeedReadyAsync(repo, $"/media/energy-semantics-{i}.flac", lufs));

            await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);
            return ids;
        }

        [Fact]
        public async Task EveryReadyTrackCarriesEnergyInUnitInterval()
        {
            var ids = await SeedReadyLibraryAsync();
            var rows = new List<EnergyRow>();
            foreach (var id in ids) rows.Add(await SelectEnergyRowAsync(db, id));

            Assert.All(rows, r => Assert.InRange(r.Energy!.Value, 0.0, 1.0));   // F80.1
        }

        [Fact]
        public async Task EnergyIsMonotoneInLufs()
        {
            var ids = await SeedReadyLibraryAsync();
            var rows = new List<EnergyRow>();
            foreach (var id in ids) rows.Add(await SelectEnergyRowAsync(db, id));

            var orderedByLufs = rows.OrderBy(r => r.IntegratedLufs).Select(r => r.Energy!.Value).ToList();
            Assert.True(IsNonDecreasing(orderedByLufs));   // F80.1 — sort by LUFS ⇒ energy non-decreasing
        }

        [Fact]
        public async Task NonReadyTracksDoNotSkewTheRank()
        {
            var ids = await SeedReadyLibraryAsync();
            var repo = Harness.Repo(db);

            var before = new List<EnergyRow>();
            foreach (var id in ids) before.Add(await SelectEnergyRowAsync(db, id));

            // A row that would be the loudest of all — and so shift every other rank upward — if it
            // counted. Flipped to 'unavailable' before the recompute: the ready population must
            // exclude it entirely, regardless of the LUFS value still sitting in its row.
            var outlierId = await SeedReadyAsync(repo, "/media/energy-semantics-outlier.flac", -1.0);
            await repo.MarkUnavailableAsync([outlierId], CancellationToken.None);
            await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

            var after = new List<EnergyRow>();
            foreach (var id in ids) after.Add(await SelectEnergyRowAsync(db, id));

            Assert.All(ids, id => Assert.Equal(   // F80.1 — population is the READY library only
                before.Single(r => r.Id == id).Energy,
                after.Single(r => r.Id == id).Energy));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — piggyback recompute (F80.2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPiggybackRecompute(DatabaseFixture db)
    {
        // Arrange (T57): a completed second-tier enrichment batch that added new LUFS rows (library
        // "doubled overnight").

        [Fact]
        public async Task PercentilesAreCorrectAfterTheBatchThatChangedLufs()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var svc = Harness.Enrichment(repo);

            // Original library: percentiles already computed once, before the batch.
            var originalLufs = new[] { -20.0, -14.0, -8.0 };
            var ids = new List<long>();
            foreach (var (lufs, i) in originalLufs.Select((l, i) => (l, i)))
                ids.Add(await SeedReadyAsync(repo, $"/media/piggyback-orig-{i}.flac", lufs));
            await svc.RecomputeEnergyPercentileAsync(CancellationToken.None);

            // The batch: the library doubles overnight — a second-tier enrichment pass writes fresh
            // LUFS for 3 new rows.
            var newLufs = new[] { -26.0, -11.0, -2.0 };
            foreach (var (lufs, i) in newLufs.Select((l, i) => (l, i)))
                ids.Add(await SeedReadyAsync(repo, $"/media/piggyback-new-{i}.flac", lufs));

            // The batch's completion point — the same call the backfill loop tick makes (SPEC F80.2).
            await svc.RecomputeEnergyPercentileAsync(CancellationToken.None);

            var wholePopulation = originalLufs.Concat(newLufs).ToList();
            var rows = new List<EnergyRow>();
            foreach (var id in ids) rows.Add(await SelectEnergyRowAsync(db, id));

            Assert.All(rows, r => Assert.Equal(   // post-batch ranks match a from-scratch computation
                ExpectedPercentRank(r.IntegratedLufs!.Value, wholePopulation), r.Energy!.Value, 4));
        }

        [Fact]
        public async Task ABatchThatTouchedNoLufsSkipsTheRecompute()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var svc = Harness.Enrichment(repo);

            var id = await SeedReadyAsync(repo, "/media/piggyback-skip.flac", -14.0);
            await svc.RecomputeEnergyPercentileAsync(CancellationToken.None);   // establishes energy once

            var xminBefore = await SelectXminAsync(db, id);

            // A second-tier batch that touches NO LUFS (cue_analyzed_at is already stamped by
            // SeedReadyAsync, so this claims nothing and never re-measures loudness) — the piggyback
            // trigger is a LUFS change, not "a batch ran".
            await svc.BackfillCueAsync(CancellationToken.None);
            await svc.RecomputeEnergyPercentileAsync(CancellationToken.None);

            var xminAfter = await SelectXminAsync(db, id);

            Assert.Equal(xminBefore, xminAfter);   // no UPDATE touched the row — the recompute was skipped
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — distribution sanity (F80.3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDistributionSanity(DatabaseFixture db)
    {
        // Arrange (T57): the demo-library fixture LUFS distribution — representative of a real mixed-
        // genre catalog (loudness-war clustering in the -15..-9 LUFS band, plus quiet/hot outliers).
        static readonly double[] DemoLibraryLufs =
        [
            -23.5, -19.8, -17.2, -16.0, -15.4, -14.9, -14.6, -14.1, -13.8, -13.5,
            -13.2, -12.9, -12.6, -12.3, -12.0, -11.6, -11.1, -10.5, -9.8, -9.1,
            -8.4, -7.6, -6.9, -5.8,
        ];

        [Fact]
        public async Task EnergySpansTheOpenUnitInterval()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var ids = new List<long>();
            foreach (var (lufs, i) in DemoLibraryLufs.Select((l, i) => (l, i)))
                ids.Add(await SeedReadyAsync(repo, $"/media/demo-library-{i}.flac", lufs));
            await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

            var energies = new List<double>();
            foreach (var id in ids) energies.Add((await SelectEnergyRowAsync(db, id)).Energy!.Value);

            Assert.True(energies.Min() < 0.1 && energies.Max() > 0.9);   // no terminal clumping
        }
    }
}
