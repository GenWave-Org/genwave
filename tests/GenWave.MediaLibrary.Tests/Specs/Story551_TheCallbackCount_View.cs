// STORY-551 — The callback count, the view half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture; add [Collection(DatabaseCollection.Name)] on activation).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheCallbackCountView
{
    sealed record RateRow
    {
        public int PersonaId { get; init; }
        public int Asks { get; init; }
        public int Used { get; init; }
        public int Hours { get; init; }
        public decimal? UsedPerHour { get; init; }
    }

    static async Task<int> SeedPersonaWithMemoryAsync(DatabaseFixture db)
    {
        await db.ResetStationAsync();
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var persona = await conn.QuerySingleAsync<int>("insert into station.persona (name) values ('Flip') returning id");
        await conn.ExecuteAsync(
            "insert into station.persona_memory (persona_id, kind, content, source) values (@persona, 'lore', 'a', 'authored')",
            new { persona });
        return persona;
    }

    static async Task<RateRow?> RateAsync(DatabaseFixture db, int persona)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<RateRow>(
            "select persona_id, asks, used, hours, used_per_hour from station.callback_rate where persona_id = @persona",
            new { persona });
    }

    // AC1 — the view
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheView(DatabaseFixture db) : IAsyncLifetime
    {
        // Given lines in 4 clock hours, 6 offers, 3 used

        int persona;

        public async Task InitializeAsync()
        {
            persona = await SeedPersonaWithMemoryAsync(db);
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            // Two lines share an hour; a fallback line in a fifth hour must not add one.
            await conn.ExecuteAsync(
                """
                insert into station.utterance (persona_id, kind, segment_kind, shift_key, text, fallback, written_at)
                select @persona, 'patter', 'LeadIn', 'a', 't', f, date_trunc('hour', now()) - make_interval(hours => h) + make_interval(mins => m)
                from (values (1, 5, false), (1, 40, false), (2, 5, false), (3, 5, false), (4, 5, false), (5, 5, true)) v(h, m, f)
                """, new { persona });
            await conn.ExecuteAsync(
                """
                insert into station.memory_offer (memory_id, persona_id, used)
                select (select id from station.persona_memory limit 1), @persona, u
                from (values (true), (true), (true), (false), (false), (false)) v(u)
                """, new { persona });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheDJsRowShows6Asks3Used4HoursAnd075UsedPerHour()
        {
            var row = await RateAsync(db, persona);

            Assert.NotNull(row);
            Assert.Equal(6, row.Asks);
            Assert.Equal(3, row.Used);
            Assert.Equal(4, row.Hours);
            Assert.Equal(0.75m, row.UsedPerHour);
        }
    }

    // AC4 — old rows don't count (sad path)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOldRowsDoNotCount(DatabaseFixture db) : IAsyncLifetime
    {
        // Given an offer 8 days old

        int persona;

        public async Task InitializeAsync()
        {
            persona = await SeedPersonaWithMemoryAsync(db);
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "insert into station.memory_offer (memory_id, persona_id, used, offered_at) " +
                "select id, persona_id, true, now() - interval '8 days' from station.persona_memory");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task The8DayOldOfferIsNotCounted()
        {
            var row = await RateAsync(db, persona);

            Assert.Null(row);
        }
    }
}
