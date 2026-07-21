// STORY-194 — Persona memory with recall windows
//
// BDD specification — xUnit (SPEC F71.4–F71.6). Postgres-backed (Category=Integration, shared
// DatabaseFixture) — the recall windows and the retention eviction are SQL-side behavior (window
// math against now(), a DELETE inside the RecordAsync transaction) that a fake store would never
// exercise honestly.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaMemoryRecall
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Fully-qualified (rather than `using Microsoft.Extensions.Options;`): this file's own
    // namespace nests under GenWave.MediaLibrary, which already has a sibling `Options` namespace
    // (ScanOptions/YearLookupOptions/LibraryOptions) — that enclosing-namespace member shadows the
    // `Microsoft.Extensions.Options.Options` static class for an unqualified `Options.Create` here.
    static PersonaMemoryRepository MemoryRepo(DatabaseFixture db, int capPerKind = 50) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions { CapPerKind = capPerKind }));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }

    /// <summary>
    /// Inserts a <c>station.persona_memory</c> row with explicit, test-controlled
    /// <c>created_at</c>/<c>last_aired_at</c> ages — direct SQL rather than
    /// <see cref="PersonaMemoryRepository.RecordAsync"/>/<see cref="PersonaMemoryRepository.MarkAiredAsync"/>,
    /// which only ever stamp "now", so recall-window scenarios can arrange rows that are hours or
    /// days old without a test that sleeps.
    /// </summary>
    static async Task<long> InsertMemoryRowAsync(
        DatabaseFixture db, long personaId, string kind, string content, string source,
        TimeSpan? airedAgo = null, TimeSpan? createdAgo = null)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            """
            insert into station.persona_memory (persona_id, kind, content, source, last_aired_at, created_at)
            values (
                @personaId, @kind, @content, @source,
                case when @airedAgoSeconds is null then null
                     else now() - make_interval(secs => @airedAgoSeconds) end,
                now() - make_interval(secs => @createdAgoSeconds)
            )
            returning id::bigint
            """,
            new
            {
                personaId,
                kind,
                content,
                source,
                airedAgoSeconds = airedAgo?.TotalSeconds,
                createdAgoSeconds = createdAgo?.TotalSeconds ?? 0,
            });
    }

    static async Task<HashSet<long>> ExistingIdsAsync(DatabaseFixture db, IEnumerable<long> ids)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<long>(
            "select id::bigint from station.persona_memory where id = any(@ids)", new { ids = ids.ToArray() });
        return rows.ToHashSet();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — anti-repeat recall (F71.4, AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAntiRepeatRecall(DatabaseFixture db)
    {
        [Fact]
        public async Task Most_recent_aired_bits_return_as_recently_done()
        {
            // Given four "bit" rows for one persona: three aired at different times in the past, and
            // one never aired at all...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Anti-Repeat DJ");
            var repo = MemoryRepo(db);

            var oldest = await InsertMemoryRowAsync(db, personaId, "bit", "old bit", "accrued", airedAgo: TimeSpan.FromDays(3));
            var middle = await InsertMemoryRowAsync(db, personaId, "bit", "middle bit", "accrued", airedAgo: TimeSpan.FromDays(1));
            var newest = await InsertMemoryRowAsync(db, personaId, "bit", "newest bit", "accrued", airedAgo: TimeSpan.FromHours(1));
            await InsertMemoryRowAsync(db, personaId, "bit", "never aired bit", "accrued", airedAgo: null);

            // When anti-repeat recall asks for the two most recently aired...
            var recalled = await repo.RecallAsync(personaId, new RecallSpec("bit", Take: 2), CancellationToken.None);

            // Then the two most recently aired bits return, newest first — the older aired bit and the
            // never-aired bit are excluded.
            Assert.Equal([newest, middle], recalled.Select(e => e.Id));
            Assert.DoesNotContain(oldest, recalled.Select(e => e.Id));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — callback window (F71.4, AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCallbackWindow(DatabaseFixture db)
    {
        static readonly RecallSpec CallbackSpec =
            new("callback", Take: 10, NotAiredWithin: TimeSpan.FromMinutes(30), CreatedWithin: TimeSpan.FromHours(24));

        [Fact]
        public async Task Two_hour_old_unaired_callback_is_offered()
        {
            // Given a callback created 2 hours ago and never aired...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Callback DJ");
            var repo = MemoryRepo(db);

            var callbackId = await InsertMemoryRowAsync(
                db, personaId, "callback", "earlier you mentioned X", "accrued",
                airedAgo: null, createdAgo: TimeSpan.FromHours(2));

            // When callback recall runs (30-minute NotAiredWithin, 24-hour CreatedWithin)...
            var recalled = await repo.RecallAsync(personaId, CallbackSpec, CancellationToken.None);

            // Then it is offered.
            Assert.Contains(callbackId, recalled.Select(e => e.Id));
        }

        [Fact]
        public async Task Callback_aired_ten_minutes_ago_is_not_offered()
        {
            // Given a callback aired 10 minutes ago — inside the 30-minute NotAiredWithin window...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Callback DJ Two");
            var repo = MemoryRepo(db);

            var callbackId = await InsertMemoryRowAsync(
                db, personaId, "callback", "already aired recently", "accrued",
                airedAgo: TimeSpan.FromMinutes(10), createdAgo: TimeSpan.FromHours(2));

            // When callback recall runs...
            var recalled = await repo.RecallAsync(personaId, CallbackSpec, CancellationToken.None);

            // Then it is not offered.
            Assert.DoesNotContain(callbackId, recalled.Select(e => e.Id));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — retention cap (F71.6, AC3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetentionCap(DatabaseFixture db)
    {
        [Fact]
        public async Task Oldest_accrued_row_evicts_in_the_record_transaction_and_authored_survive()
        {
            // Given a persona already at its accrued cap (2) for kind "bit", plus one authored row
            // that must never be touched by eviction...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Retention DJ");
            var repo = MemoryRepo(db, capPerKind: 2);

            var authoredId = await repo.RecordAsync(
                personaId, "bit", "hand-written bit", PersonaMemorySource.Authored, CancellationToken.None);
            var first = await repo.RecordAsync(
                personaId, "bit", "first accrued bit", PersonaMemorySource.Accrued, CancellationToken.None);
            var second = await repo.RecordAsync(
                personaId, "bit", "second accrued bit", PersonaMemorySource.Accrued, CancellationToken.None);

            // When a third accrued "bit" is recorded, pushing the accrued count past the cap of 2...
            var third = await repo.RecordAsync(
                personaId, "bit", "third accrued bit", PersonaMemorySource.Accrued, CancellationToken.None);

            // Then the oldest accrued row ("first") is gone, evicted inside RecordAsync's own
            // transaction, while the newer two accrued rows and the authored row all survive.
            var remaining = await ExistingIdsAsync(db, [authoredId, first, second, third]);
            Assert.Equal(new HashSet<long> { authoredId, second, third }, remaining);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — mark-before-render idempotency (F71.5, AC4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class SadPathMarkBeforeRender(DatabaseFixture db)
    {
        [Fact]
        public async Task Crash_after_mark_never_double_airs_the_callback()
        {
            // Given a callback recorded and confirmed offered by recall...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Restart DJ");
            var repo = MemoryRepo(db);
            var spec = new RecallSpec("callback", Take: 10, NotAiredWithin: TimeSpan.FromMinutes(30), CreatedWithin: TimeSpan.FromHours(24));

            var callbackId = await repo.RecordAsync(
                personaId, "callback", "a callback bit", PersonaMemorySource.Accrued, CancellationToken.None);
            Assert.Contains(callbackId, (await repo.RecallAsync(personaId, spec, CancellationToken.None)).Select(e => e.Id));

            // When it is marked aired BEFORE its render dispatches (F71.5) — and the render itself
            // never happens, simulating a crash between the mark and the air — then a fresh recall,
            // as if the host had just restarted, never offers it again inside the window.
            await repo.MarkAiredAsync(callbackId, CancellationToken.None);
            var recalledAfterRestart = await repo.RecallAsync(personaId, spec, CancellationToken.None);

            // Then that callback is not offered again.
            Assert.DoesNotContain(callbackId, recalledAfterRestart.Select(e => e.Id));
        }
    }
}
