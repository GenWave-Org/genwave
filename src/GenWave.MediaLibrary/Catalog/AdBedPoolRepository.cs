using Dapper;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// <see cref="IAdBedPool"/>'s one implementation (SPEC F168.1; STORY-403; PLAN T416) over
/// <c>library.media</c> — the same plain, non-lazy <see cref="NpgsqlDataSource"/> shape
/// <see cref="LibraryRepository"/> already uses one type over (library_svc role; unlike the
/// station-schema stores in <c>GenWave.MediaLibrary.Station</c>, this repository's own data source is
/// never behind a <see cref="Lazy{T}"/> — <c>MediaLibraryServiceCollectionExtensions</c> builds the
/// library_svc <see cref="NpgsqlDataSource"/> eagerly at startup for every consumer in this
/// namespace).
/// </summary>
sealed class AdBedPoolRepository(NpgsqlDataSource dataSource) : IAdBedPool
{
    /// <summary>
    /// The pool query, exposed so <c>tests/GenWave.MediaLibrary.Tests/Specs/Story403_AdBedPoolSql.cs</c>
    /// can text-pin every predicate term directly (the
    /// <c>GenWave.MediaLibrary.Station.AdSpotRepository.StampVoicePlanSql</c> precedent). Every jingle
    /// asset T414's pack install writes lands in <c>library.media</c> with <c>imaging_kind='jingle'</c>
    /// and a <c>jingle_role</c> of <c>bed</c>, <c>sting</c>, or <c>station_id</c> (db/45's own CHECK) —
    /// only <c>bed</c>-role rows belong to this pool. <c>state='ready'</c> and
    /// <c>unavailable_since is null</c> mirror <c>MediaRepository.PlayablePredicate</c>'s own
    /// airability gate (never touched here — a SEPARATE, narrower predicate: this pool has no opinion
    /// on rotation eligibility, only on whether the row is currently playable at all).
    /// </summary>
    internal const string PoolSql = """
        select id from library.media
        where imaging_kind = 'jingle'
          and jingle_role = 'bed'
          and state = 'ready'
          and unavailable_since is null
          and library_id = @libraryId
        order by id
        """;

    public async Task<IReadOnlyList<long>> ListReadyBedIdsAsync(long libraryId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            PoolSql, new { libraryId }, cancellationToken: ct));
        return ids.AsList();
    }
}
