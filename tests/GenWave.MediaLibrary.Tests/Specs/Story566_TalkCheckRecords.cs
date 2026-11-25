// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN T779)
//
// BDD specification — xUnit, REAL Postgres via the shared DatabaseFixture (the behaviour under test IS the
// SQL: the jsonb columns and the insert-time retention sweep). T779 activated these facts.

using System.Text.Json;
using Dapper;
using GenWave.Core.TalkRules;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkCheckRecords
{
    static readonly DateTimeOffset LocalNow = new(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(-6));

    static JsonElement Memo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    static TalkSnapshot DjLineSnapshot() =>
        new(
            TalkKinds.DjLine,
            "Banter",
            2,
            new TalkList<TalkLine>([new TalkLine("Flip", "That was a good one.")]),
            new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Flip", new TalkList<string>(["stay golden"]))]),
            null,
            null,
            LocalNow,
            "America/Edmonton",
            new TalkTrack("Heroes", "David Bowie", null, 1977, null),
            new TalkStation("GenWave", "friendly"),
            new TalkList<TalkRecentLine>([new TalkRecentLine(1, "An earlier line.", LocalNow.AddHours(-1))]),
            null,
            TalkMemos.Empty.With("repeats", Memo("""{"seen":3,"by":{"z":1,"a":[{"y":2,"x":1}]},"a":"first"}""")));

    // Verdicts sorted by rule id: that is the order a read gives them back.
    static TalkList<RuleVerdict> Verdicts() =>
        new([
            new RuleVerdict("no-numbers", SwitchedOn: false, Flag: new RuleFlag("A number", "Spell it out.", "42")),
            new RuleVerdict("no-repeats", SwitchedOn: true, Flag: null),
            new RuleVerdict("on-topic", SwitchedOn: true, Flag: new RuleFlag("Off topic", "Stay on the song.", "weather")),
        ]);

    static TalkCheckRecord Record(long? personaId) =>
        new(
            personaId,
            TalkCheckMode.Shadow,
            "RAW reply text",
            "Cleaned text",
            DjLineSnapshot(),
            Verdicts(),
            TalkLegacyOutcome.Trimmed,
            TalkOutcome.ReAsked);

    static TalkCheckRepository Repository(DatabaseFixture db, int retentionDays = 28) =>
        new(
            new Lazy<Npgsql.NpgsqlDataSource>(() => db.StationDataSource),
            new FakeOptionsMonitor<TalkCheckOptions>(new TalkCheckOptions { CheckRetentionDays = retentionDays }));

    static async Task ResetAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate table station.talk_check restart identity");
    }

    sealed record StoredRow(
        string TalkKind, string? SegmentKind, int? PersonaId, int Attempt, string Mode, string RawReply, string? Cleaned,
        string Snapshot, string Verdicts, string? LegacyOutcome, string Outcome);

    static async Task<StoredRow> OnlyRowAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<StoredRow>(
            """
            select talk_kind as TalkKind, segment_kind as SegmentKind, persona_id as PersonaId, attempt::integer as Attempt,
                   mode as Mode, raw_reply as RawReply, cleaned as Cleaned, snapshot::text as Snapshot,
                   verdicts::text as Verdicts, legacy_outcome as LegacyOutcome, outcome as Outcome
            from station.talk_check
            """);
    }

    static async Task<IReadOnlyList<long>> IdsAfterAnInsertAsync(DatabaseFixture db, int daysOld)
    {
        await ResetAsync(db);
        var repo = Repository(db, retentionDays: 28);
        await repo.AppendAsync(Record(personaId: 1), CancellationToken.None);
        await using (var conn = await db.StationDataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "update station.talk_check set checked_at = now() - make_interval(days => @daysOld)", new { daysOld });
        }

        await repo.AppendAsync(Record(personaId: 1), CancellationToken.None);

        await using var read = await db.StationDataSource.OpenConnectionAsync();
        return (await read.QueryAsync<long>("select id from station.talk_check order by id")).ToList();
    }

    // AC1 — one attempt recorded (Integration: real Postgres)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnAttemptIsRecorded(DatabaseFixture db) : IAsyncLifetime
    {
        StoredRow? stored;
        TalkCheckRecord? readBack;

        StoredRow row => stored ?? throw new InvalidOperationException("InitializeAsync did not run.");

        TalkCheckRecord record => readBack ?? throw new InvalidOperationException("InitializeAsync did not run.");

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            await repo.AppendAsync(Record(personaId: 7), CancellationToken.None);
            stored = await OnlyRowAsync(db);
            readBack = Assert.Single(await repo.ReadSinceAsync(DateTimeOffset.UtcNow.AddMinutes(-5), CancellationToken.None)).Record;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRowHoldsTheTalkKind() => Assert.Equal("dj-line", row.TalkKind);

        [Fact]
        public void TheRowHoldsTheSegmentKind() => Assert.Equal("Banter", row.SegmentKind);

        [Fact]
        public void TheRowHoldsTheDj() => Assert.Equal(7, row.PersonaId);

        [Fact]
        public void TheRowHoldsTheAttempt() => Assert.Equal(2, row.Attempt);

        [Fact]
        public void TheRowHoldsTheRawReply() => Assert.Equal("RAW reply text", row.RawReply);

        [Fact]
        public void TheRowHoldsTheCleanedText() => Assert.Equal("Cleaned text", row.Cleaned);

        // Memo keys are written sorted, so the multi-key nested memo reads back identical after jsonb reordered them.
        [Fact]
        public void TheRowHoldsTheSnapshot() => Assert.Equal(DjLineSnapshot(), record.Snapshot);

        [Fact]
        public void TheRowHoldsEveryRulesVerdict() => Assert.Equal(Verdicts(), record.Verdicts);

        [Fact]
        public void TheRowHoldsTheOutcome() => Assert.Equal("re-asked", row.Outcome);

        [Fact]
        public void TheRowHoldsTheLegacyOutcome() => Assert.Equal("trimmed", row.LegacyOutcome);

        [Fact]
        public void TheRowHoldsTheMode() => Assert.Equal("shadow", row.Mode);
    }

    // AC2 — a rule switched off
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARuleIsSwitchedOff(DatabaseFixture db) : IAsyncLifetime
    {
        JsonDocument? verdicts;

        JsonElement off => (verdicts ?? throw new InvalidOperationException("InitializeAsync did not run.")).RootElement.GetProperty("no-numbers");

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            await Repository(db).AppendAsync(Record(personaId: null), CancellationToken.None);
            verdicts = JsonDocument.Parse((await OnlyRowAsync(db)).Verdicts);
        }

        public Task DisposeAsync()
        {
            verdicts?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void ItsVerdictIsStoredMarkedOff() => Assert.False(off.GetProperty("on").GetBoolean());

        [Fact]
        public void ItsFlagIsKept() => Assert.Equal("42", off.GetProperty("flag").GetProperty("evidence").GetString());
    }

    // F220.9 (T800) — a shaper or recorder that threw is stored apart from the flag, as an extra key that is absent otherwise
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioABrokenRuleIsRecorded(DatabaseFixture db) : IAsyncLifetime
    {
        JsonDocument? verdicts;
        TalkCheckEntry? read;

        JsonElement Stored(string ruleId) =>
            (verdicts ?? throw new InvalidOperationException("InitializeAsync did not run.")).RootElement.GetProperty(ruleId);

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            var record = Record(personaId: null) with
            {
                Verdicts = new TalkList<RuleVerdict>(
                [
                    new RuleVerdict("no-repeats", SwitchedOn: true, Flag: null),
                    new RuleVerdict("on-topic", SwitchedOn: true, Flag: null, Broken: TalkRuleReasons.CouldNotShape),
                ]),
            };
            await repo.AppendAsync(record, CancellationToken.None);
            verdicts = JsonDocument.Parse((await OnlyRowAsync(db)).Verdicts);
            read = (await repo.ReadPageAsync(DateTimeOffset.UtcNow.AddDays(-1), 0, 10, CancellationToken.None)).Single();
        }

        public Task DisposeAsync()
        {
            verdicts?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheBreakageIsAnExtraKey() => Assert.Equal(TalkRuleReasons.CouldNotShape, Stored("on-topic").GetProperty("broken").GetString());

        [Fact]
        public void TheBreakageIsNotAFlag() => Assert.Equal(JsonValueKind.Null, Stored("on-topic").GetProperty("flag").ValueKind);

        [Fact]
        public void ARuleThatDidNotBreakHasNoSuchKey() => Assert.False(Stored("no-repeats").TryGetProperty("broken", out _));

        [Fact]
        public void TheBreakageReadsBack() =>
            Assert.Equal(TalkRuleReasons.CouldNotShape, read?.Record.Verdicts.Single(v => v.RuleId == "on-topic").Broken);
    }

    // AC6 — retention 28 days, a row 29 days old, one insert
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOldRowAndAnInsert(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<long>? ids;

        public async Task InitializeAsync() => ids = await IdsAfterAnInsertAsync(db, daysOld: 29);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldRowIsGone() => Assert.Equal([2L], ids);
    }

    // AC6 — a row inside the window is left alone
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARecentRowAndAnInsert(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<long>? ids;

        public async Task InitializeAsync() => ids = await IdsAfterAnInsertAsync(db, daysOld: 27);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARowTwentySevenDaysOldIsKept() => Assert.Equal([1L, 2L], ids);
    }

    // T791 (F220.26) — five rows, read in pages of two: the replay never reads a wide window in one go
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheLogIsReadInPages(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<TalkCheckEntry>? first;
        IReadOnlyList<TalkCheckEntry>? second;
        IReadOnlyList<TalkCheckEntry>? last;
        IReadOnlyList<TalkCheckEntry>? oversized;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            for (var i = 0; i < 5; i++)
            {
                await repo.AppendAsync(Record(personaId: 1), CancellationToken.None);
            }

            var since = DateTimeOffset.UtcNow.AddMinutes(-5);
            first = await repo.ReadPageAsync(since, 0, 2, CancellationToken.None);
            second = await repo.ReadPageAsync(since, first[^1].Id, 2, CancellationToken.None);
            last = await repo.ReadPageAsync(since, second[^1].Id, 2, CancellationToken.None);
            oversized = await repo.ReadPageAsync(since, 0, 100_000, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstPageHoldsTheFirstTwoRows() => Assert.Equal([1L, 2L], first?.Select(e => e.Id));

        [Fact]
        public void TheSecondPageResumesAfterTheLastId() => Assert.Equal([3L, 4L], second?.Select(e => e.Id));

        [Fact]
        public void TheLastPageHoldsTheRest() => Assert.Equal([5L], last?.Select(e => e.Id));

        [Fact]
        public void APageHoldsTheRecordTheWriterStored() => Assert.Equal("RAW reply text", first?[0].Record.RawReply);

        // Sad path
        [Fact]
        public void AnOversizedLimitStillReturnsTheRows() => Assert.Equal(5, oversized?.Count);
    }
}
