// STORY-539 / STORY-537 / STORY-538 — DJ memory recall store SQL (SPEC F218.19 · PLAN T700, amended T737)
//
// T737 retired the row-recall facts (lore sources, the 48 h aired gate, episode order, mark-aired): since SPEC
// F218.40 only a callback ask carries a memory, and its eligibility and settle facts live in
// Story550_EveryAskLeavesARecord_Store. What stays is the never-mention list.
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture — a
// throwaway postgres:16.4 container the fixture starts and tears down itself). The recall SQL
// (the 7 d never-mention window, ordering, cap) runs on now(), so only a
// real database exercises it; the fake used by GenWave.Memory.Tests mirrors it and is pinned by this file.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMemoryRecallStoreSql
{
    static readonly TimeSpan NeverMentionWindow = TimeSpan.FromDays(7);

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static PersonaMemoryRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions()));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }

    /// <summary>
    /// Inserts a persona_memory row with test-controlled ages (direct SQL: the repository only ever
    /// stamps "now"). <paramref name="occurredAt"/> is an absolute instant; the other ages are
    /// measured back from the database clock.
    /// </summary>
    static async Task<long> InsertAsync(
        DatabaseFixture db, long personaId, string kind, string content, string source = "authored",
        TimeSpan? airedAgo = null, TimeSpan? forgottenAgo = null, TimeSpan? createdAgo = null,
        DateTime? occurredAt = null, bool approved = false)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            """
            insert into station.persona_memory
                (persona_id, kind, content, source, last_aired_at, forgotten_at, created_at, occurred_at, approved_at)
            values (
                @personaId, @kind, @content, @source,
                case when @airedSecs is null then null else now() - make_interval(secs => @airedSecs) end,
                case when @forgottenSecs is null then null else now() - make_interval(secs => @forgottenSecs) end,
                now() - make_interval(secs => @createdSecs),
                @occurredAt,
                case when @approved then now() else null end
            )
            returning id::bigint
            """,
            new
            {
                personaId, kind, content, source,
                airedSecs = airedAgo?.TotalSeconds,
                forgottenSecs = forgottenAgo?.TotalSeconds,
                createdSecs = createdAgo?.TotalSeconds ?? 0,
                occurredAt,
                approved,
            });
    }

    // ---------------------------------------------------------------------
    // Persona scope (F218.22, F218.19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaScope(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> neverMention = [];

        public async Task InitializeAsync()
        {
            // Given persona B holds a life row and a forgotten row, and persona A holds its own
            await db.ResetStationAsync();
            var a = await CreatePersonaAsync(db, "Persona A");
            var b = await CreatePersonaAsync(db, "Persona B");
            await InsertAsync(db, a, MemoryKinds.Life, "A life");
            await InsertAsync(db, a, MemoryKinds.Life, "A forgotten", forgottenAgo: TimeSpan.FromHours(2));
            await InsertAsync(db, b, MemoryKinds.Life, "B life");
            await InsertAsync(db, b, MemoryKinds.Life, "B forgotten", forgottenAgo: TimeSpan.FromHours(1));

            // When persona A's never-mention list is read
            neverMention = await Repo(db).RecentlyForgottenAsync(a, NeverMentionWindow, 10, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void APersonaBForgottenClaimNeverAppearsInPersonaAsNeverMention() =>
            Assert.Equal(["A forgotten"], neverMention);
    }

    // ---------------------------------------------------------------------
    // Never-mention window, order and cap (F218.19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverMentionWindow(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> neverMention = [];

        public async Task InitializeAsync()
        {
            // Given rows forgotten 6 days, 8 days and 1 hour ago
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Window DJ");
            await InsertAsync(db, persona, MemoryKinds.Life, "six days", forgottenAgo: TimeSpan.FromDays(6));
            await InsertAsync(db, persona, MemoryKinds.Life, "eight days", forgottenAgo: TimeSpan.FromDays(8));
            await InsertAsync(db, persona, MemoryKinds.Episode, "one hour", forgottenAgo: TimeSpan.FromHours(1));

            // When the never-mention list is read for the 7 day window
            neverMention = await Repo(db).RecentlyForgottenAsync(persona, NeverMentionWindow, 10, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARowForgottenSixDaysAgoIsKept() => Assert.Contains("six days", neverMention);

        [Fact]
        public void ARowForgottenEightDaysAgoIsDropped() => Assert.DoesNotContain("eight days", neverMention);

        [Fact]
        public void ItIsNewestForgottenFirst() => Assert.Equal(["one hour", "six days"], neverMention);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverMentionCap(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> neverMention = [];

        public async Task InitializeAsync()
        {
            // Given twelve rows forgotten within the window
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Cap DJ");
            for (var i = 1; i <= 12; i++)
                await InsertAsync(db, persona, MemoryKinds.Life, $"claim {i}", forgottenAgo: TimeSpan.FromHours(i));

            // When the list is read with the 10-entry cap
            neverMention = await Repo(db).RecentlyForgottenAsync(persona, NeverMentionWindow, 10, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItHoldsAtMostTenEntries() => Assert.Equal(10, neverMention.Count);

        [Fact]
        public void ItKeepsTheNewestTen() => Assert.Equal("claim 10", neverMention[^1]);
    }
}
