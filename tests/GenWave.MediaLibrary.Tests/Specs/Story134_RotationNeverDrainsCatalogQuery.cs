// STORY-134 — Rotation never drains a playable catalog (Epic V / SPEC F41, closes gitea-#210) —
// catalog-query half. The diagnostics/orchestrator half lives in
// Orchestration.Tests/Specs/Story134_RotationRelaxationDiagnostics.cs; the feeder-window half in
// Core.Tests/Specs/Story134_FeederRecentWindowLive.cs (the STORY-131 split: facts live where
// their subject compiles, one story notwithstanding).
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection —
// the tiered ORDER BY is selection SQL, provable only against the real planner.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRotationNeverDrainsCatalogQuery
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static MediaRatingRepository RatingRepo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>Inserts a ready + measurable + eligible row in library 1 with the given artist
    /// (default shared artist "a" so recency-only facts don't accidentally trip the artist tier).</summary>
    static async Task<long> InsertSelectableRowAsync(DatabaseFixture db, string path, string? artist = "a")
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true) with { Artist = artist }, CancellationToken.None);
        return id;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASmallCatalogCyclesInsteadOfDraining(DatabaseFixture db)
    {
        [Fact]
        public async Task TwoPlayableRowsKeepYieldingCandidatesPastAFullRecentWindowCycle()
        {
            // A 2-track catalog and the default 20-entry recent window (F41.2): every draw across a
            // full window cycle (25 > 20) must still return a candidate — recency is a preference, not
            // a hard exclusion.
            await db.ResetAsync();
            await InsertSelectableRowAsync(db, "/rotation/cycle-a.flac");
            await InsertSelectableRowAsync(db, "/rotation/cycle-b.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string>();

            for (var i = 0; i < 25; i++)
            {
                var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);
                Assert.NotNull(candidate);
                recent.Add(candidate.Media.MediaId);
                if (recent.Count > 20) recent.RemoveAt(0);
            }
        }

        [Fact]
        public async Task TheSameTrackIsNeverReturnedTwiceInARowWhileASecondPlayableRowExists()
        {
            // Tier 3 (F41.3) guarantees the back-to-back guard whenever a second playable row exists
            // (F41.4) — repeated draws must never pick the same id as the immediately prior draw.
            await db.ResetAsync();
            await InsertSelectableRowAsync(db, "/rotation/no-repeat-a.flac");
            await InsertSelectableRowAsync(db, "/rotation/no-repeat-b.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string>();
            string? previousId = null;

            for (var i = 0; i < 30; i++)
            {
                var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);
                Assert.NotNull(candidate);
                if (previousId is not null)
                    Assert.NotEqual(previousId, candidate.Media.MediaId);

                previousId = candidate.Media.MediaId;
                recent.Add(candidate.Media.MediaId);
                if (recent.Count > 20) recent.RemoveAt(0);
            }
        }

        [Fact]
        public async Task AOneTrackCatalogReturnsItsOnlyRowFlaggedRepeatedRecentRatherThanNull()
        {
            // Never-silent (F1.3) outranks anti-repeat: a 1-track catalog loops its track (F41.4)
            // rather than draining, flagged so the Orchestrator can WARN (F41.5).
            await db.ResetAsync();
            var id = await InsertSelectableRowAsync(db, "/rotation/single-track.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { id.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.True(candidate.RepeatedRecent);
        }

        [Fact]
        public async Task ACandidateInsideTheRecentWindowCarriesRepeatedRecentTrue()
        {
            // Both playable rows are inside the recent window, so whichever is picked must carry
            // RepeatedRecent=true (F41.1).
            await db.ResetAsync();
            var idA = await InsertSelectableRowAsync(db, "/rotation/inside-window-a.flac");
            var idB = await InsertSelectableRowAsync(db, "/rotation/inside-window-b.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { idA.ToString(), idB.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.True(candidate.RepeatedRecent);
        }

        [Fact]
        public async Task ACandidateOutsideTheRecentWindowCarriesRepeatedRecentFalse()
        {
            // Only one row is outside the recent window — tier 1 (F41.3) deterministically picks it,
            // and it must carry RepeatedRecent=false.
            await db.ResetAsync();
            var idA = await InsertSelectableRowAsync(db, "/rotation/outside-window-a.flac");
            var idB = await InsertSelectableRowAsync(db, "/rotation/outside-window-b.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { idA.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.False(candidate.RepeatedRecent);
            Assert.Equal(idB.ToString(), candidate.Media.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a genuine drain is still a drain
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAGenuineDrainIsStillADrain(DatabaseFixture db)
    {
        [Fact]
        public async Task ZeroPlayableRowsInScopeReturnsNull()
        {
            // No rows at all — the F41.2 zero-playable-pool null contract.
            await db.ResetAsync();

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var candidate = await catalog.GetRotationCandidateAsync(scope, [], artistSeparation: 2, CancellationToken.None);

            Assert.Null(candidate);
        }

        [Fact]
        public async Task AnEmptyScopeStillReturnsNullPerDefaultDeny()
        {
            // A playable row exists, but the caller's scope is empty — default-deny wins, no SQL issued.
            await db.ResetAsync();
            await InsertSelectableRowAsync(db, "/rotation/empty-scope.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);

            var candidate = await catalog.GetRotationCandidateAsync(LibraryScope.None, [], artistSeparation: 2, CancellationToken.None);

            Assert.Null(candidate);
        }

        [Fact]
        public async Task NeverPlayRowsAreInvisibleToTheRotationCandidateQuery()
        {
            // The only playable row is flagged never_play (F33.6) — the playable predicate excludes it,
            // so the pool is genuinely empty and null is correct, not a relaxation.
            await db.ResetAsync();
            var id = await InsertSelectableRowAsync(db, "/rotation/never-play-only.flac");
            await RatingRepo(db).SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var candidate = await catalog.GetRotationCandidateAsync(scope, [], artistSeparation: 2, CancellationToken.None);

            Assert.Null(candidate);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStrictCallersKeepStrictSemantics(DatabaseFixture db)
    {
        [Fact]
        public async Task GetRandomReadyAsyncStillReturnsNullWhenEveryReadyRowIsExcluded()
        {
            // Regression (F41.7): GetRandomReadyAsync is byte-identical — /media/random's strict
            // exclusion contract never relaxes, even though GetRotationCandidateAsync now exists.
            await db.ResetAsync();
            var id = await InsertSelectableRowAsync(db, "/rotation/strict-exclude.flac");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var reference = await catalog.GetRandomReadyAsync(scope, [id.ToString()], CancellationToken.None);

            Assert.Null(reference);
        }
    }
}
