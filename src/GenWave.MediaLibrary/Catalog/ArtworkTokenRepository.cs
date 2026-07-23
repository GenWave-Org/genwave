using System.Security.Cryptography;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IArtworkTokenStore"/> (SPEC F88.2, gh-#105,
/// STORY-222) over <c>library.media.artwork_token</c>. Connection-per-call against the library's
/// own <see cref="NpgsqlDataSource"/>, mirroring <see cref="MediaLibraryMembershipRepository"/>'s
/// wiring — singleton-safe with no captive dependency.
///
/// <para>
/// Race safety: <see cref="GetOrCreateTokenAsync"/> always generates a fresh candidate token and
/// sends a single <c>UPDATE … SET artwork_token = coalesce(artwork_token, @candidate) …
/// RETURNING</c> statement — the same "one round trip IS the existence-and-write check" shape
/// <see cref="MediaRatingRepository"/> uses for its lazy upsert. Two concurrent first-asks for the
/// same row take Postgres's ordinary row-level write lock: whichever UPDATE commits first wins the
/// coalesce, and the second (blocked until the first commits, then re-reading the now-committed
/// row under READ COMMITTED) sees the already-persisted token and returns it instead of its own
/// discarded candidate. Neither caller ever needs to retry or detect a conflict — the database
/// serializes it for free.
/// </para>
/// </summary>
sealed class ArtworkTokenRepository(NpgsqlDataSource dataSource) : IArtworkTokenStore
{
    // 32 lowercase hex chars = 16 bytes = 128 bits (SPEC F88.2).
    const int TokenLength = 32;

    public async Task<string> GetOrCreateTokenAsync(long mediaId, CancellationToken ct)
    {
        var candidate = RandomNumberGenerator.GetHexString(TokenLength, lowercase: true);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var token = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            update library.media
            set artwork_token = coalesce(artwork_token, @candidate)
            where id = @mediaId
            returning artwork_token
            """,
            new { mediaId, candidate },
            cancellationToken: ct));

        return token ?? throw new InvalidOperationException(
            $"library.media id {mediaId} not found — cannot mint an artwork token for a nonexistent row.");
    }

    public async Task<ArtworkTokenResolution?> ResolveAsync(string token, CancellationToken ct)
    {
        if (!IsWellFormed(token))
            return null;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(long Id, string Path)?>(new CommandDefinition(
            "select id, path from library.media where artwork_token = @token",
            new { token },
            cancellationToken: ct));

        return row is null ? null : new ArtworkTokenResolution(row.Value.Id, row.Value.Path);
    }

    /// <summary>
    /// The F88.2 non-enumerability guard: rejects anything that is not exactly
    /// <see cref="TokenLength"/> lowercase hex characters before a single query is issued — a
    /// malformed token (wrong length, uppercase, non-hex) can never justify a database round trip.
    /// </summary>
    static bool IsWellFormed(string token)
    {
        if (token.Length != TokenLength)
            return false;

        foreach (var c in token)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;

        return true;
    }
}
