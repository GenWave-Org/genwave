using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="IUtteranceStore"/> over <c>station.utterance</c> (SPEC F218, STORY-532).
/// Same shape as <see cref="BoothLogRepository"/>: lazy station_svc data source, retention swept in the
/// same transaction as the insert, window read live from <see cref="UtteranceOptions"/>.
/// </summary>
sealed class UtteranceRepository(Lazy<NpgsqlDataSource> dataSource, IOptionsMonitor<UtteranceOptions> options)
    : IUtteranceStore
{
    const string SweepSql =
        "delete from station.utterance where written_at < now() - make_interval(days => @RetentionDays)";

    public async Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            insert into station.utterance (persona_id, show_id, kind, segment_kind, shift_key, text, fallback)
            values (@PersonaId, @ShowId, @Kind::station.utterance_kind, @SegmentKind, @ShiftKey, @Text, @Fallback)
            returning id::bigint
            """,
            new
            {
                utterance.PersonaId,
                utterance.ShowId,
                Kind = utterance.Kind.ToString().ToLowerInvariant(),
                utterance.SegmentKind,
                ShiftKey = utterance.ShiftKey.ToString(),
                utterance.Text,
                utterance.Fallback,
            },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            SweepSql, RetentionParameters(), transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task MarkAiredAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update station.utterance set aired_at = now() where id = @Id and aired_at is null",
            new { Id = id },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<UtteranceRow>(new CommandDefinition(
            """
            select id::bigint as id, persona_id::bigint as persona_id, show_id::bigint as show_id,
                   kind::text as kind, segment_kind, shift_key, text, fallback, written_at, aired_at
            from station.utterance
            where persona_id = @PersonaId
            order by written_at desc, id desc
            limit @Take
            """,
            new { PersonaId = personaId, Take = take },
            cancellationToken: ct));

        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(SweepSql, RetentionParameters(), cancellationToken: ct));
    }

    object RetentionParameters() => new { RetentionDays = options.CurrentValue.UtteranceRetentionDays };

    /// <summary>Dapper projection: <c>timestamptz</c> reads back as <see cref="DateTime"/> (UTC), converted at the boundary.</summary>
    sealed record UtteranceRow(
        long Id, long PersonaId, long? ShowId, string Kind, string SegmentKind, string ShiftKey,
        string Text, bool Fallback, DateTime WrittenAt, DateTime? AiredAt)
    {
        public UtteranceEntry ToEntry() => new(
            Id, PersonaId, ShowId, Enum.Parse<UtteranceKind>(Kind, ignoreCase: true), SegmentKind, ParseShiftKey(),
            Text, Fallback, AsUtc(WrittenAt), AiredAt is { } aired ? AsUtc(aired) : null);

        GenWave.Core.Domain.ShiftKey ParseShiftKey() =>
            GenWave.Core.Domain.ShiftKey.TryParse(ShiftKey, out var key)
                ? key
                : throw new InvalidOperationException($"station.utterance row {Id} has an unparseable shift_key '{ShiftKey}'.");

        static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
