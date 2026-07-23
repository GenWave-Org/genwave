using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IMediaLibraryMembership"/> (gh-#99) over
/// <c>library.media</c>. Connection-per-call against the library's own
/// <see cref="NpgsqlDataSource"/>, mirroring <see cref="MediaRatingRepository"/>'s wiring —
/// singleton-safe with no captive dependency. One indexed-PK SELECT, nothing else: this seam
/// answers "which of these ids are in these libraries" and deliberately never grows row
/// projection.
/// </summary>
sealed class MediaLibraryMembershipRepository(NpgsqlDataSource dataSource) : IMediaLibraryMembership
{
    public async Task<IReadOnlySet<long>> FilterToLibrariesAsync(
        IReadOnlyCollection<long> mediaIds, LibraryScope libraries, CancellationToken ct)
    {
        if (mediaIds.Count == 0 || libraries.IsEmpty)
            return new HashSet<long>();

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<long>(new CommandDefinition(
            "select id from library.media where id = any(@ids) and library_id = any(@libraryIds)",
            new { ids = mediaIds.Distinct().ToArray(), libraryIds = libraries.LibraryIds.ToArray() },
            cancellationToken: ct));

        return rows.ToHashSet();
    }
}
