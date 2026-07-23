// STORY-224 — Anyone can ask; nobody can probe (DB-backed half)
//
// BDD specification — xUnit (SPEC F87.1, F87.3, F87.8). Postgres-backed (Category=Integration,
// shared DatabaseFixture) — the insert-time retention sweep, the pending-cap eviction, and the
// status CHECK constraint's teeth are real-DB behavior a fake store would never exercise
// honestly. T86 lands db/24 (folded into db/06 for this fixture, per its own remarks) +
// RequestRepository — exactly what T87's intake endpoint needs (Insert-with-sweep, CountPending,
// EvictOldestPending). The endpoint itself is Host.Tests' own Story224_RequestIntake.cs, T87's scope
// — this file owns none of that surface, only the store beneath it.

using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRequestStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RequestRepository Repo(DatabaseFixture db, int wishRetentionHours = 24) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), wishRetentionHours);

    /// <summary>
    /// Dapper-mapped projection for this file's own direct-SQL assertions. Settable properties, not
    /// a positional record — <c>Moods</c>' <c>text[]</c> column reports as the general
    /// <see cref="Array"/> CLR type through the reader (rather than the concrete <c>string[]</c>),
    /// which Dapper's stricter constructor-matching (used for positional records — the shape
    /// <see cref="PersonaTasteRow"/>/<see cref="BoothLogRepository"/>'s row records use, neither of
    /// which projects an array column) rejects; the property-setter path this shape falls back to
    /// coerces it instead, mirroring <see cref="Catalog.MediaRow.Moods"/>'s own settable-property
    /// convention. <c>DateTime</c>, not <c>DateTimeOffset</c> — Npgsql's default <c>timestamptz</c>
    /// reader shape.
    /// </summary>
    sealed record RequestRow
    {
        public string? Wish { get; init; }
        public string? Artist { get; init; }
        public string? Title { get; init; }
        public string[]? Moods { get; init; }
        public string Status { get; init; } = "";
        public DateTime ReceivedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
    }

    static async Task<RequestRow> ReadRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<RequestRow>(
            """
            select wish, artist, title, moods, status, received_at, expires_at
            from station.request where id = @id
            """,
            new { id });
    }

    static async Task<int> CountAllRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.request");
    }

    /// <summary>
    /// Inserts a fully-populated row directly (bypassing the repository) so the retention/eviction
    /// scenarios can control <c>received_at</c> and pre-set parsed predicates/status the repository
    /// itself never writes (that is the parser/matcher's job, T88/T89).
    /// </summary>
    static async Task<long> InsertRawRowAsync(
        DatabaseFixture db, string? wish, string? artist, string? title, string[]? moods,
        string status, TimeSpan receivedAgo)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into station.request (received_at, wish, artist, title, moods, status, expires_at)
            values (now() - make_interval(secs => @receivedAgoSeconds), @wish, @artist, @title,
                    @moods, @status, now() + interval '15 minutes')
            returning id
            """,
            new
            {
                receivedAgoSeconds = receivedAgo.TotalSeconds,
                wish, artist, title, moods, status,
            });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — insert round trip (F87.1, AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioInsert(DatabaseFixture db)
    {
        [Fact]
        public async Task A_new_wish_lands_pending_with_the_given_expiry()
        {
            // Given no prior requests...
            await db.ResetRequestAsync();
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var repo = Repo(db);

            // When one wish is inserted...
            var id = await repo.InsertAsync("something dreamier by Zeppelin", expiresAt, CancellationToken.None);

            // Then it lands pending, holding the wish text and the caller's expiry.
            var row = await ReadRowAsync(db, id);
            Assert.Equal("something dreamier by Zeppelin", row.Wish);
            Assert.Equal("pending", row.Status);
            Assert.Equal(expiresAt.UtcDateTime, row.ExpiresAt, TimeSpan.FromSeconds(1));
            Assert.True(row.ReceivedAt > DateTime.UtcNow.AddMinutes(-1));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — insert-time wish retention sweep (F87.8)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetentionSweep(DatabaseFixture db)
    {
        [Fact]
        public async Task Insert_nulls_wish_text_on_rows_older_than_retention_but_keeps_predicates_and_outcome()
        {
            // Given an old row past the 24h retention window, already parsed and matched...
            await db.ResetRequestAsync();
            var oldId = await InsertRawRowAsync(
                db, wish: "something dreamier by Zeppelin", artist: "Led Zeppelin", title: null,
                moods: ["chill", "dreamy"], status: "unmatched", receivedAgo: TimeSpan.FromHours(48));
            var repo = Repo(db, wishRetentionHours: 24);

            // When a new wish is inserted (the sweep runs inside that SAME insert's transaction)...
            await repo.InsertAsync("a new wish", DateTimeOffset.UtcNow.AddMinutes(15), CancellationToken.None);

            // Then the old row's wish text is gone, but its parsed predicates and outcome survive.
            var oldRow = await ReadRowAsync(db, oldId);
            Assert.Null(oldRow.Wish);
            Assert.Equal("Led Zeppelin", oldRow.Artist);
            Assert.NotNull(oldRow.Moods);
            Assert.Equal(["chill", "dreamy"], oldRow.Moods);
            Assert.Equal("unmatched", oldRow.Status);
        }

        [Fact]
        public async Task A_row_inside_the_retention_window_keeps_its_wish_text()
        {
            // Given a recent row, well inside a 24h retention window...
            await db.ResetRequestAsync();
            var recentId = await InsertRawRowAsync(
                db, wish: "still fresh", artist: null, title: null, moods: null,
                status: "pending", receivedAgo: TimeSpan.FromHours(1));
            var repo = Repo(db, wishRetentionHours: 24);

            // When a new wish is inserted (the sweep runs, but has nothing old to touch)...
            await repo.InsertAsync("a new wish", DateTimeOffset.UtcNow.AddMinutes(15), CancellationToken.None);

            // Then the recent row's wish text is untouched.
            var recentRow = await ReadRowAsync(db, recentId);
            Assert.Equal("still fresh", recentRow.Wish);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — pending count (F87.3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCountPending(DatabaseFixture db)
    {
        [Fact]
        public async Task Only_pending_rows_are_counted()
        {
            // Given two pending rows and one already fulfilled...
            await db.ResetRequestAsync();
            await InsertRawRowAsync(db, "a", null, null, null, "pending", TimeSpan.Zero);
            await InsertRawRowAsync(db, "b", null, null, null, "pending", TimeSpan.Zero);
            await InsertRawRowAsync(db, "c", null, null, null, "fulfilled", TimeSpan.Zero);
            var repo = Repo(db);

            // When the pending count is read...
            var count = await repo.CountPendingAsync(CancellationToken.None);

            // Then only the two pending rows are counted.
            Assert.Equal(2, count);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — oldest-pending eviction (F87.3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEvictOldestPending(DatabaseFixture db)
    {
        [Fact]
        public async Task Eviction_removes_exactly_the_oldest_pending_row()
        {
            // Given three pending rows at distinct ages, oldest first...
            await db.ResetRequestAsync();
            var oldestId = await InsertRawRowAsync(db, "oldest", null, null, null, "pending", TimeSpan.FromMinutes(30));
            var middleId = await InsertRawRowAsync(db, "middle", null, null, null, "pending", TimeSpan.FromMinutes(20));
            var newestId = await InsertRawRowAsync(db, "newest", null, null, null, "pending", TimeSpan.FromMinutes(10));
            var repo = Repo(db);

            // When the pending cap evicts...
            await repo.EvictOldestPendingAsync(CancellationToken.None);

            // Then exactly the oldest row is gone; the other two remain untouched.
            Assert.Equal(2, await CountAllRowsAsync(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadRowAsync(db, oldestId));
            Assert.Equal("middle", (await ReadRowAsync(db, middleId)).Wish);
            Assert.Equal("newest", (await ReadRowAsync(db, newestId)).Wish);
        }

        [Fact]
        public async Task Eviction_is_a_no_op_when_no_pending_row_exists()
        {
            // Given only a non-pending row...
            await db.ResetRequestAsync();
            await InsertRawRowAsync(db, "already fulfilled", null, null, null, "fulfilled", TimeSpan.FromMinutes(30));
            var repo = Repo(db);

            // When eviction runs...
            await repo.EvictOldestPendingAsync(CancellationToken.None);

            // Then nothing was removed — there was no pending row to evict.
            Assert.Equal(1, await CountAllRowsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the status CHECK constraint has teeth
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStatusCheckConstraint(DatabaseFixture db)
    {
        [Fact]
        public async Task An_unrecognized_status_is_rejected_by_the_database_itself()
        {
            // Given no prior requests...
            await db.ResetRequestAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a direct INSERT tries a status outside the CHECK's four-value set...
            // Then the database itself rejects it — regardless of what the repository would ever write.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.request (status, expires_at)
                values ('bogus', now() + interval '15 minutes')
                """));
        }
    }
}
