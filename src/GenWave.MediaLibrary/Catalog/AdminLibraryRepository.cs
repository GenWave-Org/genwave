using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Admin write operations against <c>library.library</c> (STORY-046 / STORY-047, Epic J).
/// Implements <see cref="IAdminLibraryWrite"/>: create, rename, and delete libraries.
///
/// Uniqueness is enforced by the DB-level <c>UNIQUE(name)</c> constraint (L1 migration).
/// Duplicate-name attempts raise a Postgres 23505 (unique_violation) which is caught here
/// and mapped to <see cref="LibraryWriteResult.NameConflict"/> — no application-level pre-flight
/// SELECT is performed (see task spec: "leverage the constraint").
///
/// FK-violation (23503) on DELETE is caught and mapped to
/// <see cref="LibraryWriteResult.HasDependents"/> after a follow-up COUNT query to
/// populate the body. Using the exception-first approach: the common path (empty library)
/// is a single round-trip; the count query only fires when a FK violation occurs.
/// </summary>
sealed class AdminLibraryRepository(NpgsqlDataSource dataSource, IStationEventSink? events = null) : IAdminLibraryWrite
{
    // LibraryMutated publish seam (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    // Postgres SQLSTATE codes — well-known constants; no Npgsql.PostgresErrorCodes dependency.
    const string UniqueViolation    = "23505";
    const string ForeignKeyViolation = "23503";

    public async Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "insert into library.library (name) values (@name) returning id",
                new { name },
                cancellationToken: ct));
            events.Publish(new LibraryMutated("created", id));
            return new LibraryWriteResult.Created(id);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new LibraryWriteResult.NameConflict();
        }
    }

    public async Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                "update library.library set name = @name where id = @id",
                new { id, name },
                cancellationToken: ct));
            if (affected == 0)
                return new LibraryWriteResult.NotFound();
            events.Publish(new LibraryMutated("renamed", id));
            return new LibraryWriteResult.Renamed();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new LibraryWriteResult.NameConflict();
        }
    }

    public async Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        try
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                "delete from library.library where id = @id",
                new { id },
                cancellationToken: ct));
            if (affected == 0)
                return new LibraryWriteResult.NotFound();
            events.Publish(new LibraryMutated("deleted", id));
            return new LibraryWriteResult.Deleted();
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            // The FK on library.media.library_id is ON DELETE RESTRICT — the library still has media.
            // Query the count after the fact to populate the 409 body.
            var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*)::int from library.media where library_id = @id",
                new { id },
                cancellationToken: ct));
            return new LibraryWriteResult.HasDependents(count);
        }
    }
}
