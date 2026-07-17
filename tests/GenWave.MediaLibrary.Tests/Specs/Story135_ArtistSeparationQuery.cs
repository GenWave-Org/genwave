// STORY-135 — No same artist back to back (Epic V / SPEC F41.3, closes gitea-#213) — catalog-query
// half. The live-settings half lives in Orchestration.Tests/Specs/Story135_ArtistSeparationLive.cs.
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection — the artist
// tier's correlated subquery over the recent ids is selection SQL, provable only against the real
// planner.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureArtistSeparationQuery
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Inserts a ready + measurable + eligible row in library 1 with the given artist
    /// (nullable, so blank-artist facts can seed a genuinely null artist).</summary>
    static async Task<long> InsertSelectableRowAsync(DatabaseFixture db, string path, string? artist)
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
    public sealed class ScenarioArtistSeparationHoldsWhenHonorable(DatabaseFixture db)
    {
        [Fact]
        public async Task TheCandidatesArtistDiffersFromTheLastTwoSelectionsArtistsAtDepthTwo()
        {
            // Recent artists {A, B}; the only row that is both non-recent AND artist-clear (D) must
            // win over a non-recent row that repeats a recent artist (F41.3).
            await db.ResetAsync();
            var idA = await InsertSelectableRowAsync(db, "/artist-sep/depth-two-a.flac", "Artist A");
            var idB = await InsertSelectableRowAsync(db, "/artist-sep/depth-two-b.flac", "Artist B");
            await InsertSelectableRowAsync(db, "/artist-sep/depth-two-c.flac", "Artist A"); // non-recent, repeats A
            var idD = await InsertSelectableRowAsync(db, "/artist-sep/depth-two-d.flac", "Artist D"); // non-recent, clear

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { idA.ToString(), idB.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(idD.ToString(), candidate.Media.MediaId);
        }

        [Fact]
        public async Task ArtistComparisonIsCaseInsensitive()
        {
            // The only non-recent row's artist differs only by case from the recent artist — tier 2
            // must still flag it as a repeat (F41.3).
            await db.ResetAsync();
            var idA = await InsertSelectableRowAsync(db, "/artist-sep/case-a.flac", "Muse");
            var idB = await InsertSelectableRowAsync(db, "/artist-sep/case-b.flac", "MUSE");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { idA.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 1, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(idB.ToString(), candidate.Media.MediaId);
            Assert.True(candidate.RepeatedArtist);
        }

        [Fact]
        public async Task SeparationDepthZeroDisablesTheArtistTier()
        {
            // artistSeparation=0 must supply an empty comparison set: the non-recent row's flag reads
            // false even though its artist genuinely matches the recent entry's artist (F41.6).
            await db.ResetAsync();
            var idA = await InsertSelectableRowAsync(db, "/artist-sep/disabled-a.flac", "Rex");
            var idB = await InsertSelectableRowAsync(db, "/artist-sep/disabled-b.flac", "Rex");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { idA.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 0, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(idB.ToString(), candidate.Media.MediaId);
            Assert.False(candidate.RepeatedArtist);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioArtistRepetitionBeatsTrackRepetition(DatabaseFixture db)
    {
        [Fact]
        public async Task ANonRecentSameArtistRowWinsOverARecentDifferentArtistRow()
        {
            // The only non-recent row repeats the last selection's artist; a recent row has a
            // different artist. Tier 1 (recency) is more binding than tier 2 (artist), so the
            // non-recent same-artist row wins (F41.3).
            await db.ResetAsync();
            var bowieOlder = await InsertSelectableRowAsync(db, "/artist-sep/precedence-older.flac", "Prince");
            var bowieRecent = await InsertSelectableRowAsync(db, "/artist-sep/precedence-recent.flac", "Bowie");
            var nonRecentSameArtist = await InsertSelectableRowAsync(db, "/artist-sep/precedence-repeat.flac", "Bowie");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { bowieOlder.ToString(), bowieRecent.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(nonRecentSameArtist.ToString(), candidate.Media.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — blank artists never participate
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBlankArtistsNeverParticipate(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullArtistCandidateIsNeverPenalizedByTheArtistTier()
        {
            // The only non-recent row has a null artist — it can never be flagged as a repeat, no
            // matter what the recent artist set contains (F41.3).
            await db.ResetAsync();
            var recentRow = await InsertSelectableRowAsync(db, "/artist-sep/null-candidate-recent.flac", "Elton John");
            var nullArtistRow = await InsertSelectableRowAsync(db, "/artist-sep/null-candidate.flac", null);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { recentRow.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 1, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(nullArtistRow.ToString(), candidate.Media.MediaId);
            Assert.False(candidate.RepeatedArtist);
        }

        [Fact]
        public async Task ANullArtistRecentEntryNeverPenalizesAnyCandidate()
        {
            // The most-recent selection has a null artist — a non-recent candidate's real artist can
            // never be flagged as matching a blank (F41.3).
            await db.ResetAsync();
            var nullArtistRecent = await InsertSelectableRowAsync(db, "/artist-sep/null-recent.flac", null);
            var candidateRow = await InsertSelectableRowAsync(db, "/artist-sep/null-recent-candidate.flac", "Solange");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { nullArtistRecent.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 1, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(candidateRow.ToString(), candidate.Media.MediaId);
            Assert.False(candidate.RepeatedArtist);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASingleArtistLibraryStillPlays(DatabaseFixture db)
    {
        [Fact]
        public async Task ASingleArtistCatalogReturnsACandidateFlaggedRepeatedArtist()
        {
            // Every playable row shares one artist — the candidate is still returned (never null), just
            // flagged RepeatedArtist=true so the Orchestrator can WARN (F41.5).
            await db.ResetAsync();
            var justAired = await InsertSelectableRowAsync(db, "/artist-sep/single-artist-a.flac", "Solo Artist");
            var onlyAlternative = await InsertSelectableRowAsync(db, "/artist-sep/single-artist-b.flac", "Solo Artist");

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var recent = new List<string> { justAired.ToString() };

            var candidate = await catalog.GetRotationCandidateAsync(scope, recent, artistSeparation: 2, CancellationToken.None);

            Assert.NotNull(candidate);
            Assert.Equal(onlyAlternative.ToString(), candidate.Media.MediaId);
            Assert.True(candidate.RepeatedArtist);
        }
    }
}
