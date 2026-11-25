using Dapper;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="ITalkCheckStore"/> over <c>station.talk_check</c> (SPEC F220.29, STORY-566).
/// Same shape as <see cref="UtteranceRepository"/>: lazy station_svc data source, retention swept in the same
/// transaction as the insert, window read live from <see cref="TalkCheckOptions"/>.
/// </summary>
sealed class TalkCheckRepository(Lazy<NpgsqlDataSource> dataSource, IOptionsMonitor<TalkCheckOptions> options)
    : ITalkCheckStore, ITalkFlagReader
{
    const string SweepSql =
        "delete from station.talk_check where checked_at < now() - make_interval(days => @RetentionDays)";

    const string SelectColumns =
        """
        select id, checked_at, persona_id::bigint as persona_id, mode, raw_reply, cleaned,
               snapshot::text as snapshot, verdicts::text as verdicts, legacy_outcome, outcome
        from station.talk_check

        """;

    // A flag = the verdict is on and holds a flag object whose reason is not "Could not check" (TalkRuleReasons.CouldNotCheck), the
    // same test as db/55's view. A broken shaper/recorder is stored apart from the flag, so it needs no exclusion.
    const string CountFlagsSql =
        """
        select v.key as rule_id, c.talk_kind, count(*)::int as flags
        from station.talk_check c
        cross join lateral jsonb_each(c.verdicts) v
        where c.checked_at >= @Since
          and v.value ->> 'on' = 'true'
          and jsonb_typeof(v.value -> 'flag') = 'object'
          and v.value -> 'flag' ->> 'reason' <> @CouldNotCheck
        group by v.key, c.talk_kind
        """;

    const string LatestFlagsSql =
        """
        select c.id, c.checked_at, coalesce(p.name, nullif(c.snapshot -> 'ad' ->> 'sponsorName', '')) as dj, c.talk_kind,
               c.verdicts -> @RuleId::text -> 'flag' ->> 'evidence' as words, c.outcome
        from station.talk_check c
        left join station.persona p on p.id = c.persona_id
        where c.checked_at >= @Since
          and c.verdicts -> @RuleId::text ->> 'on' = 'true'
          and jsonb_typeof(c.verdicts -> @RuleId::text -> 'flag') = 'object'
          and c.verdicts -> @RuleId::text -> 'flag' ->> 'reason' <> @CouldNotCheck
        order by c.checked_at desc, c.id desc
        limit @Limit
        """;

    public async Task AppendAsync(TalkCheckRecord record, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.talk_check
                (talk_kind, segment_kind, persona_id, attempt, mode, raw_reply, cleaned,
                 snapshot, verdicts, legacy_outcome, outcome)
            values
                (@TalkKind, @SegmentKind, @PersonaId, @Attempt, @Mode, @RawReply, @Cleaned,
                 @Snapshot::jsonb, @Verdicts::jsonb, @LegacyOutcome, @Outcome)
            """,
            new
            {
                TalkKind = TalkStoredTokens.Of(record.Snapshot.Kind),
                record.Snapshot.SegmentKind,
                // station.persona.id is a serial (integer), so the narrowing is lossless for any real id.
                PersonaId = record.PersonaId is { } id ? (int?)checked((int)id) : null,
                Attempt = checked((short)record.Snapshot.Attempt),
                Mode = TalkStoredTokens.Of(record.Mode),
                record.RawReply,
                record.Cleaned,
                Snapshot = TalkSnapshotJson.Serialize(record.Snapshot),
                Verdicts = TalkVerdictsJson.Serialize(record.Verdicts),
                LegacyOutcome = record.LegacyOutcome is { } legacy ? TalkStoredTokens.Of(legacy) : null,
                Outcome = TalkStoredTokens.Of(record.Outcome),
            },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            SweepSql, new { RetentionDays = options.CurrentValue.CheckRetentionDays }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TalkCheckRow>(new CommandDefinition(
            SelectColumns + """
            where checked_at >= @Since
            order by checked_at, id
            """,
            new { Since = since },
            cancellationToken: ct));

        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<IReadOnlyList<TalkCheckEntry>> ReadPageAsync(
        DateTimeOffset since, long afterId, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TalkCheckRow>(new CommandDefinition(
            SelectColumns + """
            where checked_at >= @Since and id > @AfterId
            order by id
            limit @Limit
            """,
            new { Since = since, AfterId = afterId, Limit = Math.Clamp(limit, 1, ITalkCheckStore.MaxPageSize) },
            cancellationToken: ct));

        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<IReadOnlyList<TalkRuleFlagCount>> CountFlagsAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<FlagCountRow>(new CommandDefinition(
            CountFlagsSql,
            new { Since = since, CouldNotCheck = TalkRuleReasons.CouldNotCheck },
            cancellationToken: ct));

        return rows.Select(r => new TalkRuleFlagCount(r.RuleId, TalkStoredTokens.Parse<TalkKinds>(r.TalkKind), r.Flags)).ToList();
    }

    public async Task<IReadOnlyList<TalkFlaggedAttempt>> LatestFlagsAsync(string ruleId, DateTimeOffset since, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<FlaggedAttemptRow>(new CommandDefinition(
            LatestFlagsSql,
            new { RuleId = ruleId, Since = since, CouldNotCheck = TalkRuleReasons.CouldNotCheck, Limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: ct));

        return rows
            .Select(r => new TalkFlaggedAttempt(
                r.Id,
                new DateTimeOffset(DateTime.SpecifyKind(r.CheckedAt, DateTimeKind.Utc)),
                r.Dj,
                TalkStoredTokens.Parse<TalkKinds>(r.TalkKind),
                r.Words ?? string.Empty,
                TalkStoredTokens.Parse<TalkOutcome>(r.Outcome)))
            .ToList();
    }

    sealed record FlagCountRow(string RuleId, string TalkKind, int Flags);

    sealed record FlaggedAttemptRow(long Id, DateTime CheckedAt, string? Dj, string TalkKind, string? Words, string Outcome);

    /// <summary>Dapper projection: <c>timestamptz</c> reads back as <see cref="DateTime"/> (UTC), converted at the boundary.</summary>
    sealed record TalkCheckRow(
        long Id, DateTime CheckedAt, long? PersonaId, string Mode, string RawReply, string? Cleaned,
        string Snapshot, string Verdicts, string? LegacyOutcome, string Outcome)
    {
        public TalkCheckEntry ToEntry() => new(
            Id,
            new DateTimeOffset(DateTime.SpecifyKind(CheckedAt, DateTimeKind.Utc)),
            new TalkCheckRecord(
                PersonaId,
                TalkStoredTokens.Parse<TalkCheckMode>(Mode),
                RawReply,
                Cleaned,
                TalkSnapshotJson.Deserialize(Snapshot),
                TalkVerdictsJson.Deserialize(Verdicts),
                LegacyOutcome is { } legacy ? TalkStoredTokens.Parse<TalkLegacyOutcome>(legacy) : null,
                TalkStoredTokens.Parse<TalkOutcome>(Outcome)));
    }
}
