// STORY-222 — artwork_token seam (SPEC F88.2, PLAN T83)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection —
// mirrors Story110_RatingPersistence's shape. db/23 + the lazy-generation catalog member +
// token→media resolution on the library connection.

using Dapper;
using GenWave.MediaLibrary.Catalog;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureArtworkTokenSeam
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static ArtworkTokenRepository TokenRepo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>Inserts a fresh library.media row (state='discovered') and returns its id.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
              VALUES (@path, 'flac', 1024, now(), 'discovered', 1)
              RETURNING id",
            new { path });
    }

    static async Task<string?> ReadStoredTokenAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "select artwork_token from library.media where id = @id", new { id });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — lazy, stable tokens
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFirstNeedMintsAToken(DatabaseFixture db)
    {
        [Fact]
        public async Task FirstNeedGeneratesARandomTokenOnce()
        {
            // Given a row with no token. When the token is first requested, then a 128-bit value
            // (32 lowercase hex chars) is generated and persisted.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/artwork/first-need.flac");
            var repo = TokenRepo(db);

            var token = await repo.GetOrCreateTokenAsync(id, CancellationToken.None);

            Assert.Matches("^[0-9a-f]{32}$", token);
            Assert.Equal(token, await ReadStoredTokenAsync(db, id));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSubsequentReads(DatabaseFixture db)
    {
        [Fact]
        public async Task SubsequentReadsReturnTheSameToken()
        {
            // A second GetOrCreateTokenAsync call on the same row returns the identical token —
            // it never re-mints one once persisted.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/artwork/stable.flac");
            var repo = TokenRepo(db);

            var first = await repo.GetOrCreateTokenAsync(id, CancellationToken.None);
            var second = await repo.GetOrCreateTokenAsync(id, CancellationToken.None);

            Assert.Equal(first, second);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — token → media resolution
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTokenResolution(DatabaseFixture db)
    {
        [Fact]
        public async Task TokensResolveBackToTheirMediaPath()
        {
            // A minted token resolves to an ArtworkTokenResolution carrying the row's id and path.
            await db.ResetAsync();
            const string path = "/artwork/resolve.flac";
            var id = await InsertMediaRowAsync(db, path);
            var repo = TokenRepo(db);
            var token = await repo.GetOrCreateTokenAsync(id, CancellationToken.None);

            var resolution = await repo.ResolveAsync(token, CancellationToken.None);

            Assert.NotNull(resolution);
            Assert.Equal(id, resolution.MediaId);
            Assert.Equal(path, resolution.Path);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown / malformed tokens never resolve
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnknownToken(DatabaseFixture db)
    {
        [Fact]
        public async Task AWellFormedButUnstoredTokenResolvesToNull()
        {
            // A syntactically valid token that was never minted resolves to null — not an error.
            await db.ResetAsync();
            var repo = TokenRepo(db);

            var resolution = await repo.ResolveAsync("0123456789abcdef0123456789abcdef", CancellationToken.None);

            Assert.Null(resolution);
        }

        [Fact]
        public async Task AMalformedTokenResolvesToNullWithoutTouchingTheDatabase()
        {
            // Wrong length / uppercase / non-hex — rejected by the guard before any SQL runs.
            await db.ResetAsync();
            var repo = TokenRepo(db);

            var resolution = await repo.ResolveAsync("NOT-A-VALID-TOKEN", CancellationToken.None);

            Assert.Null(resolution);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the unique index has teeth
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUniquenessIsEnforced(DatabaseFixture db)
    {
        [Fact]
        public async Task TheUniqueIndexRejectsADuplicateToken()
        {
            // A direct SQL UPDATE trying to set a second row's token to an already-used value
            // violates the media_artwork_token unique index (DB's own teeth), regardless of what
            // the repository itself would ever do.
            await db.ResetAsync();
            var firstId = await InsertMediaRowAsync(db, "/artwork/dupe-first.flac");
            var secondId = await InsertMediaRowAsync(db, "/artwork/dupe-second.flac");
            var duplicate = Guid.NewGuid().ToString("N"); // 32 lowercase hex chars

            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "update library.media set artwork_token = @duplicate where id = @firstId",
                new { duplicate, firstId });

            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update library.media set artwork_token = @duplicate where id = @secondId",
                new { duplicate, secondId }));
        }
    }
}
