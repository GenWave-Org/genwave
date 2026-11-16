// STORY-542 — The week after, in numbers, the repository half (SPEC F218.29 · PLAN T708)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture). RepeatNumbersRepository
// reads the two db/06 views; the view maths themselves are pinned in tests/GenWave.Host.Tests/Specs/Story542_*.cs.

using Dapper;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheWeekAfterInNumbersRepository
{
    static RepeatNumbersRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<RepeatNumbersRepository>.Instance);

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheRepositoryReadsTheViews(DatabaseFixture db) : IAsyncLifetime
    {
        GenWave.Core.Domain.RepeatNumbers? numbers;

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var flip = await conn.QuerySingleAsync<int>(
                """
                insert into station.persona (name, definition)
                values ('Flip', '{"quirkRules":[{"text":"Says \"banger, log it\"","maxPerShift":1}]}'::jsonb)
                returning id
                """);
            await conn.QuerySingleAsync<int>("insert into station.persona (name) values ('Tony') returning id");
            await conn.ExecuteAsync(
                """
                insert into station.utterance (persona_id, kind, segment_kind, shift_key, text, fallback)
                values (@flip, 'patter', 'LeadIn', 'a', 'Banger, log it. Banger, log it.', false),
                       (@flip, 'patter', 'LeadIn', 'b', 'Banger, log it.', true)
                """, new { flip });
            await conn.ExecuteAsync(
                """
                insert into station.booth_log (kind, summary, persona_id)
                values ('repeat-caught', 'Repeat caught: "banger, log it" — trimmed', @flip)
                """, new { flip });
            numbers = await Repo(db).ReadAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OnlyDjsWithRepeatActivityAreListed()
        {
            var dj = Assert.Single((numbers ?? throw new InvalidOperationException("no numbers")).Djs);
            Assert.Equal("Flip", dj.PersonaName);
            Assert.Equal((1, 1, 1), (dj.Catches, dj.Trims, dj.Fallbacks));
        }

        [Fact]
        public void OnlyShiftsOverTheLimitAreListed()
        {
            var over = Assert.Single((numbers ?? throw new InvalidOperationException("no numbers")).Overused);
            Assert.Equal("a", over.ShiftKey);
            Assert.Equal("banger, log it", over.Phrase);
            Assert.Equal((1, 2), (over.MaxPerShift, over.Occurrences));
        }
    }
}
