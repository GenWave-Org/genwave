// STORY-539 / STORY-537 / STORY-538 — DJ memory recall store SQL (SPEC F218.19, F218.22 · PLAN T700)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture — a
// throwaway postgres:16.4 container the fixture starts and tears down itself). The recall SQL
// (eligibility, the 48 h aired gate, the 7 d never-mention window, ordering) runs on now(), so only a
// real database exercises it; the fake used by GenWave.Memory.Tests mirrors it and is pinned by this file.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMemoryRecallStoreSql
{
    static readonly TimeSpan RecallGap = TimeSpan.FromHours(48);
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

    static async Task<(int AiredCount, DateTime? LastAiredAt)> AiredStateAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(int, DateTime?)>(
            "select aired_count, last_aired_at from station.persona_memory where id = @id", new { id });
    }

    static async Task<IReadOnlyList<string>> ContentsAsync(
        DatabaseFixture db, long personaId, string kind, MemoryRecallOrder order, int take = 10) =>
        (await Repo(db).CandidatesAsync(personaId, new(kind, take, RecallGap, order), CancellationToken.None))
            .Select(m => m.Content).ToList();

    // ---------------------------------------------------------------------
    // Lore sources (F218.22, STORY-538)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLoreSources(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> recalled = [];

        public async Task InitializeAsync()
        {
            // Given one lore row of each source (the drafted one both approved and pending)
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Lore DJ");
            await InsertAsync(db, persona, MemoryKinds.Lore, "authored lore", source: "authored");
            await InsertAsync(db, persona, MemoryKinds.Lore, "approved draft", source: "drafted", approved: true);
            await InsertAsync(db, persona, MemoryKinds.Lore, "pending draft", source: "drafted");
            await InsertAsync(db, persona, MemoryKinds.Lore, "accrued lore", source: "accrued");
            await InsertAsync(db, persona, MemoryKinds.Lore, "extracted lore", source: "extracted");

            // When lore is recalled
            recalled = await ContentsAsync(db, persona, MemoryKinds.Lore, MemoryRecallOrder.LeastRecentlyAired);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AnAuthoredRowIsRecalled() => Assert.Contains("authored lore", recalled);

        [Fact]
        public void AnApprovedDraftIsRecalled() => Assert.Contains("approved draft", recalled);

        [Fact]
        public void APendingDraftIsNotRecalled() => Assert.DoesNotContain("pending draft", recalled);

        [Fact]
        public void AnAccruedRowIsNotRecalled() => Assert.DoesNotContain("accrued lore", recalled);

        [Fact]
        public void AnExtractedRowIsNotRecalled() => Assert.DoesNotContain("extracted lore", recalled);
    }

    // ---------------------------------------------------------------------
    // The 48 h aired gate and forgotten rows (F218.22, F218.19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAiredGateAndForgotten(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> recalled = [];

        public async Task InitializeAsync()
        {
            // Given life rows aired 47 h ago, aired 49 h ago, never aired, and forgotten
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Gate DJ");
            await InsertAsync(db, persona, MemoryKinds.Life, "aired 47h", airedAgo: TimeSpan.FromHours(47));
            await InsertAsync(db, persona, MemoryKinds.Life, "aired 49h", airedAgo: TimeSpan.FromHours(49));
            await InsertAsync(db, persona, MemoryKinds.Life, "never aired");
            await InsertAsync(db, persona, MemoryKinds.Life, "forgotten", forgottenAgo: TimeSpan.FromHours(1));

            // When life rows are recalled
            recalled = await ContentsAsync(db, persona, MemoryKinds.Life, MemoryRecallOrder.LeastRecentlyAired);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARowAired47HoursAgoIsSkipped() => Assert.DoesNotContain("aired 47h", recalled);

        [Fact]
        public void ARowAired49HoursAgoIsRecalled() => Assert.Contains("aired 49h", recalled);

        [Fact]
        public void ANeverAiredRowIsRecalled() => Assert.Contains("never aired", recalled);

        [Fact]
        public void AForgottenRowIsNeverRecalled() => Assert.DoesNotContain("forgotten", recalled);

        [Fact]
        public void NeverAiredComesBeforeLeastRecentlyAired() =>
            Assert.Equal(["never aired", "aired 49h"], recalled);
    }

    // ---------------------------------------------------------------------
    // Episode order and the RecalledMemory mapping (F218.22)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEpisodeOrder(DatabaseFixture db) : IAsyncLifetime
    {
        static readonly DateTime FiveDaysAgo = DateTime.UtcNow.AddDays(-5);
        // Whole seconds, so the round trip through timestamptz (microseconds) is exact.
        static readonly DateTime TenMinutesAgo = new(DateTime.UtcNow.AddMinutes(-10).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        long fixedInstantId;
        IReadOnlyList<RecalledMemory> recalled = [];

        public async Task InitializeAsync()
        {
            // Given three episodes: one that happened 5 days ago, one at 10 minutes ago (the newest
            // occurred_at), and one with no occurred_at created 1 hour ago
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Episode DJ");
            await InsertAsync(db, persona, MemoryKinds.Episode, "five days ago", occurredAt: FiveDaysAgo);
            fixedInstantId = await InsertAsync(db, persona, MemoryKinds.Episode, "fixed instant",
                occurredAt: TenMinutesAgo);
            await InsertAsync(db, persona, MemoryKinds.Episode, "created an hour ago", createdAgo: TimeSpan.FromHours(1));

            // When episodes are recalled newest first
            recalled = await Repo(db).CandidatesAsync(persona,
                new(MemoryKinds.Episode, 10, RecallGap, MemoryRecallOrder.NewestFirst), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheyComeNewestFirstFallingBackToCreatedAt() =>
            Assert.Equal(["fixed instant", "created an hour ago", "five days ago"], recalled.Select(m => m.Content));

        [Fact]
        public void ARowMapsToARecalledMemoryIncludingOccurredAt() =>
            Assert.Equal(
                new RecalledMemory(fixedInstantId, MemoryKinds.Episode, "fixed instant", TenMinutesAgo),
                recalled.Single(m => m.Id == fixedInstantId));

        [Fact]
        public void ARowWithoutOccurredAtMapsToNull() =>
            Assert.Null(recalled.Single(m => m.Content == "created an hour ago").OccurredAt);
    }

    // ---------------------------------------------------------------------
    // Persona scope (F218.22, F218.19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaScope(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> candidates = [];
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

            // When persona A's life rows and never-mention list are read
            candidates = await ContentsAsync(db, a, MemoryKinds.Life, MemoryRecallOrder.LeastRecentlyAired);
            neverMention = await Repo(db).RecentlyForgottenAsync(a, NeverMentionWindow, 10, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void APersonaBRowNeverAppearsForPersonaA() => Assert.Equal(["A life"], candidates);

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

    // ---------------------------------------------------------------------
    // Marking aired (F71.5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMarkAired(DatabaseFixture db) : IAsyncLifetime
    {
        (int AiredCount, DateTime? LastAiredAt) first, second, untouched;

        public async Task InitializeAsync()
        {
            // Given three never-aired rows
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Mark DJ");
            var a = await InsertAsync(db, persona, MemoryKinds.Life, "first");
            var b = await InsertAsync(db, persona, MemoryKinds.Life, "second");
            var c = await InsertAsync(db, persona, MemoryKinds.Life, "untouched");

            // When two of them are marked aired in one call
            await Repo(db).MarkAiredAsync([a, b], CancellationToken.None);
            first = await AiredStateAsync(db, a);
            second = await AiredStateAsync(db, b);
            untouched = await AiredStateAsync(db, c);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EachMarkedRowsAiredCountIncrements() => Assert.Equal([1, 1], [first.AiredCount, second.AiredCount]);

        [Fact]
        public void EachMarkedRowGetsALastAiredAt() => Assert.All([first.LastAiredAt, second.LastAiredAt], t => Assert.NotNull(t));

        [Fact]
        public void AnUnmarkedRowIsLeftAlone() => Assert.Equal((0, (DateTime?)null), untouched);
    }
}
