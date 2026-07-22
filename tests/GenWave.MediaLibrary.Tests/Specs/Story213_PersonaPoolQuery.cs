// STORY-213 — The persona ranks inside the law
//
// BDD specification — xUnit (SPEC F82.2). PLAN T64 — the pool-shaped catalog read
// GenWave.Orchestration.RankerPersonaPickProvider needs: T61's exact by-construction
// envelope+rotation-tier filtering (GetEnvelopeCandidateAsync), widened to LIMIT @limit rows, plus
// energy/moods per row that GetEnvelopeCandidateAsync's own MediaReference projection never carries.
// (The predicate-filtering half of this story is already fully proven by
// Story212_EnvelopeCandidateQuery — this file only proves what T64 actually adds: pool size and the
// two extra columns.)
//
// Integration: hits real Postgres via DatabaseCollection — mirrors Story212_EnvelopeCandidateQuery's
// own rationale (selection SQL is provable only against the real planner).

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaPoolQuery
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static async Task<long> InsertReadyAsync(
        MediaRepository repo, string path, string? genre = "g", double lufs = -14.0)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, new EnrichmentResult(
            DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: "t", Artist: "a", Album: "al", AlbumArtist: "aa", Genre: genre, TrackNo: 1, Year: 2020,
            IntegratedLufs: lufs, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow), CancellationToken.None);
        return id;
    }

    public static class ScenarioPoolSize
    {
        // Arrange (T64): more envelope-conforming rows than the caller's limit.

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioMoreRowsThanLimit(DatabaseFixture db)
        {
            [Fact]
            public async Task ReturnsExactlyLimitRowsFromTheEnvelopeConstrainedPool()
            {
                // F82.2: a pool query, not a single candidate — limit caps it, never a partial
                // widening past the envelope's own genre allow-list.
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                for (var i = 0; i < 5; i++)
                    await InsertReadyAsync(repo, $"/pool/rock-{i}.flac", genre: "Rock");
                await InsertReadyAsync(repo, "/pool/jazz.flac", genre: "Jazz");
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                    scope, [], artistSeparation: 0, envelope, limit: 3, CancellationToken.None);

                Assert.Equal(3, pool.Count);
                Assert.All(pool, row => Assert.Equal("Rock", row.Media.Genre));
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioFewerRowsThanLimit(DatabaseFixture db)
        {
            [Fact]
            public async Task NeverReturnsMoreRowsThanThePoolActuallyHas()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                await InsertReadyAsync(repo, "/pool/only-rock.flac", genre: "Rock");
                await InsertReadyAsync(repo, "/pool/jazz.flac", genre: "Jazz");
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                    scope, [], artistSeparation: 0, envelope, limit: 18, CancellationToken.None);

                var row = Assert.Single(pool);
                Assert.Equal("Rock", row.Media.Genre);
            }
        }
    }

    public static class ScenarioRowShape
    {
        // Arrange (T64): one row with a measured energy percentile and written moods.

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioEnergyAndMoods(DatabaseFixture db)
        {
            [Fact]
            public async Task EachRowCarriesEnergyAndMoods()
            {
                // MediaReference alone never carries either field (T62 review note) — the pool row
                // must, since PersonaRanker's score/taste-match formulas both need them. Two rows
                // (not one) so percent_rank() over the ready population produces a non-degenerate
                // 0.0/1.0 split — a lone ready row always ranks 0.0 (Postgres' percent_rank()
                // definition for a single-row partition), which would prove nothing about the
                // column actually carrying a real measured percentile.
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                await InsertReadyAsync(repo, "/pool/quiet.flac", genre: "Rock", lufs: -30.0);
                var id = await InsertReadyAsync(repo, "/pool/tagged.flac", genre: "Rock", lufs: -10.0);
                await repo.WriteMoodsAsync(id, ["driving", "warm"], CancellationToken.None);
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                    scope, [], artistSeparation: 0, envelope, limit: 18, CancellationToken.None);

                Assert.Equal(2, pool.Count);
                var row = Assert.Single(pool, r => r.Media.MediaId == id.ToString());
                Assert.Equal(1.0, row.Energy); // the louder of the two rows ⇒ percent_rank() = 1.0
                Assert.Equal(["driving", "warm"], row.Moods);
            }

            [Fact]
            public async Task EnergyLagStillAdmitsTheRowWithANullEnergy()
            {
                // SPEC F81.4/F80.2: a row that landed after the last population-wide recompute
                // carries NULL energy but is still ready + measurable + fully selectable — the pool
                // query must admit it (same enrichment-lag-never-silences exemption
                // GetEnvelopeCandidateAsync's own energy-band predicate honors), just with a null
                // Energy the ranker treats as neutral.
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                var id = await InsertReadyAsync(repo, "/pool/lag.flac", genre: "Rock");
                // No RecomputeEnergyPercentilesAsync call — energy stays NULL.

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                    scope, [], artistSeparation: 0, envelope, limit: 18, CancellationToken.None);

                var row = Assert.Single(pool);
                Assert.Equal(id.ToString(), row.Media.MediaId);
                Assert.Null(row.Energy);
                Assert.Empty(row.Moods);
            }
        }
    }
}
