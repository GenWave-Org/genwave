using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IShowImagingScope"/> (SPEC F115.4, STORY-305, PLAN
/// T240) over <c>library.media</c>. Connection-per-call against the library's own
/// <see cref="NpgsqlDataSource"/>, mirroring <see cref="MediaLibraryMembershipRepository"/>'s wiring —
/// singleton-safe with no captive dependency.
/// </summary>
sealed class ShowImagingScopeRepository(NpgsqlDataSource dataSource) : IShowImagingScope
{
    /// <summary>
    /// One statement: <c>UPDATE ... RETURNING</c> both clears <c>show_id</c> and names what it
    /// cleared in the SAME round trip — no separate SELECT-then-UPDATE. There is no FK here for a
    /// second writer to race against (F117.1), so the single statement isn't closing a TOCTOU gap so
    /// much as it is simply the smallest honest shape for a seam that does one thing.
    /// </summary>
    public async Task<IReadOnlyList<ScopedImagingRow>> UnscopeAsync(long showId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long Id, string? Title)>(new CommandDefinition(
            """
            update library.media set show_id = null
            where show_id = @showId
            returning id, title
            """,
            new { showId },
            cancellationToken: ct));

        return rows.OrderBy(r => r.Id).Select(r => new ScopedImagingRow(r.Id, r.Title)).ToList();
    }
}
