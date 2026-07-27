using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

/// <summary>
/// Shared arrange-step helper for Story240_ScheduleStore.cs and Story242_UpgradeChangesNothing.cs —
/// both files insert a bare <c>station.persona</c> row (name only, every other column takes its
/// table default) purely to have a real id for a <c>station.segment_schedule.persona_id</c> foreign
/// key to point at. Byte-identical between the two files before this extraction; a change to one
/// would silently drift from the other without this single source.
/// </summary>
static class ScheduleTestPersonas
{
    public static async Task<long> InsertAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }
}
