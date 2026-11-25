using System.Collections.Frozen;
using Dapper;
using GenWave.Core.TalkRules;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="ITalkRuleSwitches"/> over <c>station.talk_rule_switch</c> (SPEC F220.22, F220.23).
/// The store is dumb: it keeps whatever (rule, kind) it is given; refusing a kind a rule cannot cover is the
/// switchboard's job (GenWave.TalkRules knows the rules, this project does not).
/// </summary>
/// <remarks>
/// Reads come from one immutable snapshot of every row, loaded on first read and swapped out whole. A write
/// clears it only after the upsert has committed, so the next read sees the change with no restart. A load that
/// raced a write is never cached: the generation moves on every clear and a loader only publishes if it has not.
/// A failed write throws and leaves the cache alone (nothing changed); a failed load throws to the caller.
/// </remarks>
sealed class TalkRuleSwitchRepository(Lazy<NpgsqlDataSource> dataSource) : ITalkRuleSwitches
{
    static readonly IReadOnlyDictionary<string, bool> NoSwitches = FrozenDictionary<string, bool>.Empty;

    readonly object gate = new();
    Snapshot? cached;
    long generation;

    public async ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct)
    {
        var snapshot = await CurrentAsync(ct);
        return snapshot.ByKind.TryGetValue(kind, out var forKind) ? forKind : NoSwitches;
    }

    public async ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct)
    {
        var token = TalkStoredTokens.Of(kind);
        await using (var conn = await dataSource.Value.OpenConnectionAsync(ct))
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.talk_rule_switch (rule_id, talk_kind, enabled)
                values (@RuleId, @Kind, @Enabled)
                on conflict (rule_id, talk_kind) do update set enabled = excluded.enabled, changed_at = now()
                """,
                new { RuleId = ruleId, Kind = token, Enabled = enabled },
                cancellationToken: ct));
        }

        lock (gate)
        {
            generation++;
            cached = null;
        }
    }

    async ValueTask<Snapshot> CurrentAsync(CancellationToken ct)
    {
        long seen;
        lock (gate)
        {
            if (cached is { } hit)
            {
                return hit;
            }

            seen = generation;
        }

        var loaded = await LoadAsync(ct);
        lock (gate)
        {
            if (generation == seen)
            {
                cached = loaded;
            }
        }

        return loaded;
    }

    async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<SwitchRow>(new CommandDefinition(
            "select rule_id as RuleId, talk_kind as TalkKind, enabled as Enabled from station.talk_rule_switch",
            cancellationToken: ct));

        var byKind = rows
            .GroupBy(row => TalkStoredTokens.Parse<TalkKinds>(row.TalkKind))
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, bool>)group.ToFrozenDictionary(row => row.RuleId, row => row.Enabled, StringComparer.Ordinal));
        return new Snapshot(byKind);
    }

    sealed record Snapshot(IReadOnlyDictionary<TalkKinds, IReadOnlyDictionary<string, bool>> ByKind);

    sealed record SwitchRow(string RuleId, string TalkKind, bool Enabled);
}
