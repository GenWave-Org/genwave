// STORY-560 AC3 (store half) — the 7-day late-talk count reads station.booth_log (SPEC F219.26, PLAN T767)
//
// Postgres-backed (Category=Integration, shared DatabaseFixture): the kind filter and the time bound are SQL.

using Dapper;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkDroppedLateCount
{
    static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    static async Task InsertRowAsync(DatabaseFixture db, string kind, DateTimeOffset occurredAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into station.booth_log (occurred_at, kind, summary) values (@occurredAt, @kind, 'row')",
            new { occurredAt, kind });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRowsOfSeveralAges(DatabaseFixture db)
    {
        [Fact]
        public async Task OnlyLateTalkRowsInsideTheWindowAreCounted()
        {
            await db.ResetBoothLogAsync();
            await InsertRowAsync(db, "talk-dropped-late", Now.AddDays(-1));
            await InsertRowAsync(db, "talk-dropped-late", Now.AddDays(-6));
            await InsertRowAsync(db, "talk-dropped-late", Now.AddDays(-8));
            await InsertRowAsync(db, "voice-missing", Now.AddDays(-1));
            var counter = new TalkDroppedLateRepository(
                new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<TalkDroppedLateRepository>.Instance);

            Assert.Equal(2, await counter.CountSinceAsync(Now.AddDays(-7), CancellationToken.None));
        }
    }
}
