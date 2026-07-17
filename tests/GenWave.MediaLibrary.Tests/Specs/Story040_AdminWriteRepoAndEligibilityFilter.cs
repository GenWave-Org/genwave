// STORY-040 (repo half) — IAdminMediaWrite UPDATE behavior + eligibility selection filter
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres).
// Mirrors Story032_CatalogProjectsEnergyColumns for the write side.
// Covers:
//   • UpdateAsync returns Updated / Conflict / NotFound correctly (OutOfScope retired by SPEC
//     F43.2, Epic V, closes gitea-#203 — scope no longer gates the single-row write)
//   • eligible=false rows are never returned by GetRandomReadyAsync
//   • all-ineligible → GetRandomReadyAsync returns null (no selection = null, not crash)
//
// The HTTP-layer scenarios (auth, ETag over HTTP, on-air round-trip) live in
// GenWave.Host.Tests/Specs/Story040_EditTagsAndEligibilityViaPatch.cs (operator-gated).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdminWriteRepoAndEligibilityFilter
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — UpdateAsync returns Updated
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpdateAppliesNonNullFields(DatabaseFixture db)
    {
        [Fact]
        public async Task UpdateReturnsUpdatedAndWritesTagColumns()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/write-tags.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Read current version.
            var xmin = await ReadXminAsync(db, id);
            var patch = new MediaPatch(Title: "New Title", Artist: "New Artist", Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);
            var scope = new LibraryScope([1L]);

            var result = await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, xmin, scope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.Updated, result);

            // Verify columns persisted.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var row = await conn.QuerySingleAsync<(string title, string artist)>(
                "select title, artist from library.media where id = @id", new { id });
            Assert.Equal("New Title", row.title);
            Assert.Equal("New Artist", row.artist);
        }

        [Fact]
        public async Task UpdateSetsTagsEditedAtWhenAnyTagFieldIsPresent()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/tags-edited-at.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var xmin = await ReadXminAsync(db, id);
            var patch = new MediaPatch(Title: "Stamped", Artist: null, Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);
            var scope = new LibraryScope([1L]);

            await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, xmin, scope, CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var tagsEditedAt = await conn.ExecuteScalarAsync<DateTime?>(
                "select tags_edited_at from library.media where id = @id", new { id });
            Assert.NotNull(tagsEditedAt);
        }

        [Fact]
        public async Task UpdateDoesNotSetTagsEditedAtWhenOnlyEligibleChanges()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/eligible-only.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var xmin = await ReadXminAsync(db, id);
            // Only eligible — no tag field set.
            var patch = new MediaPatch(Title: null, Artist: null, Album: null, Genre: null, Year: null, Eligible: false, LibraryId: null);
            var scope = new LibraryScope([1L]);

            await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, xmin, scope, CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var tagsEditedAt = await conn.ExecuteScalarAsync<DateTime?>(
                "select tags_edited_at from library.media where id = @id", new { id });
            Assert.Null(tagsEditedAt);
        }

        [Fact]
        public async Task UpdateSetsEligibleFalseCorrectly()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/mark-ineligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var xmin = await ReadXminAsync(db, id);
            var patch = new MediaPatch(Title: null, Artist: null, Album: null, Genre: null, Year: null, Eligible: false, LibraryId: null);
            var scope = new LibraryScope([1L]);

            var result = await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, xmin, scope, CancellationToken.None);
            Assert.Equal(MediaWriteResult.Updated, result);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var eligible = await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id });
            Assert.False(eligible);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAD PATH — optimistic concurrency conflict
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStaleVersionYieldsConflict(DatabaseFixture db)
    {
        [Fact]
        public async Task UpdateWithWrongXminReturnsConflict()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/conflict-test.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Use a deliberately wrong (stale) xmin token.
            const string staleVersion = "0";
            var patch = new MediaPatch(Title: "Should Not Persist", Artist: null, Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);
            var scope = new LibraryScope([1L]);

            var result = await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, staleVersion, scope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.Conflict, result);

            // The title must not have changed.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var title = await conn.ExecuteScalarAsync<string?>(
                "select title from library.media where id = @id", new { id });
            // Original title from Harness.ReadyResult is "t"
            Assert.Equal("t", title);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAD PATH — row absent → NotFound
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAbsentRowYieldsNotFound(DatabaseFixture db)
    {
        [Fact]
        public async Task UpdateWithNonExistentIdReturnsNotFound()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var patch = new MediaPatch(Title: "Ghost", Artist: null, Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);
            var scope = new LibraryScope([1L]);

            // id 999999 does not exist.
            var result = await ((IAdminMediaWrite)repo).UpdateAsync("999999", patch, "0", scope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.NotFound, result);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPEC F43.2 (Epic V, closes gitea-#203) supersedes the old "row in different library → OutOfScope"
    // fact: the single-row write no longer gates on scope at all — it reaches any existing row by
    // id regardless of the caller's LibraryScope (scope is a curation filter, not an access gate).
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRowOutsideScopeStillUpdates(DatabaseFixture db)
    {
        [Fact]
        public async Task UpdateWithScopeNotContainingRowLibraryStillReturnsUpdated()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Row belongs to library 1 (the default).
            var id = await repo.InsertDiscoveredAsync("/media/scope-test.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var xmin = await ReadXminAsync(db, id);
            var patch = new MediaPatch(Title: "Not Forbidden", Artist: null, Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);

            // Scope that does NOT include library 1 — no longer a gate (SPEC F43.2).
            var wrongScope = new LibraryScope([99L]);

            var result = await ((IAdminMediaWrite)repo).UpdateAsync(id.ToString(), patch, xmin, wrongScope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.Updated, result);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STORY-103 — UpdateReturningVersionAsync's returned token is the real post-update xmin
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReturningVersionIsPostUpdateAndChainable(DatabaseFixture db)
    {
        [Fact]
        public async Task UpdateReturningVersionYieldsPostUpdateXminThatChains()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/chain.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var v0 = await ReadXminAsync(db, id);
            var scope = new LibraryScope([1L]);

            var first = await ((IAdminMediaWrite)repo).UpdateReturningVersionAsync(
                id.ToString(), new MediaPatch("A", null, null, null, null, null, null), v0, scope, CancellationToken.None);
            Assert.Equal(MediaWriteResult.Updated, first.Result);

            // AC3: the returned token IS the row's actual post-update xmin (not the pre-update one).
            var vAfter = await ReadXminAsync(db, id);
            Assert.Equal(vAfter, first.NewVersion);
            Assert.NotEqual(v0, first.NewVersion);

            // AC4: chaining the returned token as the next expectedVersion succeeds — no 409.
            var next = first.NewVersion ?? throw new InvalidOperationException("Updated must carry a version.");
            var second = await ((IAdminMediaWrite)repo).UpdateReturningVersionAsync(
                id.ToString(), new MediaPatch("B", null, null, null, null, null, null), next, scope, CancellationToken.None);
            Assert.Equal(MediaWriteResult.Updated, second.Result);
        }

        [Fact]
        public async Task NoOpPatchReturnsUpdatedWithTheCurrentXminUnchanged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/noop-chain.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var v0 = await ReadXminAsync(db, id);
            var scope = new LibraryScope([1L]);

            // All-null patch — the no-op branch: no UPDATE is issued, only a SELECT that also
            // reads xmin, so the no-op case still materializes a version (STORY-103's third
            // RETURNING-adjacent path, alongside the two UPDATE-with-RETURNING branches above).
            var outcome = await ((IAdminMediaWrite)repo).UpdateReturningVersionAsync(
                id.ToString(), new MediaPatch(null, null, null, null, null, null, null), v0, scope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.Updated, outcome.Result);
            // No write happened, so the row's xmin is unchanged — the no-op reports the CURRENT version.
            Assert.Equal(v0, outcome.NewVersion);

            var vAfter = await ReadXminAsync(db, id);
            Assert.Equal(vAfter, outcome.NewVersion);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ELIGIBILITY FILTER — GetRandomReadyAsync excludes ineligible rows
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIneligibleRowNeverSelectedByRandom(DatabaseFixture db)
    {
        [Fact]
        public async Task IneligibleReadyRowIsNeverReturnedByGetRandomReady()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/ineligible-track.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Mark the row ineligible directly via SQL.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("update library.media set eligible = false where id = @id", new { id });

            var scope = new LibraryScope([1L]);

            // Try 10 times to guard against flaky false-negatives in random selection.
            for (var i = 0; i < 10; i++)
            {
                var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
                Assert.Null(reference);
            }
        }

        [Fact]
        public async Task AllIneligibleRowsReturnNullFromGetRandomReady()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Insert two ready rows and mark both ineligible.
            for (var i = 0; i < 2; i++)
            {
                var rowId = await repo.InsertDiscoveredAsync($"/media/all-ineligible-{i}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(rowId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            }

            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("update library.media set eligible = false");

            var scope = new LibraryScope([1L]);
            var result = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task EligibleRowIsReturnedWhenIneligibleRowAlsoExists()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var ineligibleId = await repo.InsertDiscoveredAsync("/media/ineligible-mix.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var eligibleId = await repo.InsertDiscoveredAsync("/media/eligible-mix.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(eligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);

            // With only one eligible row, every random pick must return it.
            for (var i = 0; i < 5; i++)
            {
                var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
                Assert.NotNull(reference);
                Assert.Equal(eligibleId.ToString(), reference.MediaId);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Reads the current <c>xmin</c> (Postgres row version) for a media row as a string.</summary>
    static async Task<string> ReadXminAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string>(
            "select xmin::text from library.media where id = @id", new { id }) ?? "";
    }
}
