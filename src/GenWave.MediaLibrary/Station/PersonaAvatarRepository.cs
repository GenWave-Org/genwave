using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaAvatarStore"/> (SPEC F128-F129, STORY-333,
/// PLAN T290) over <c>station.persona_avatar</c>. Connection-per-query, mirrors
/// <see cref="ThemeRepository"/>'s own station-schema wiring; a single-table upsert never needs
/// <see cref="FontPackRepository"/>'s own multi-table transaction.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema store
/// in this file's directory: merely resolving <see cref="IPersonaAvatarStore"/> from DI must never be
/// enough to trigger a connection attempt against an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class PersonaAvatarRepository(Lazy<NpgsqlDataSource> dataSource) : IPersonaAvatarStore
{
    // persona_id is int4 in Postgres (db/37) but the C# seam is long (the house
    // int4-column-behind-long-C#-seam convention) — cast explicitly on read, the same way
    // PersonaTasteRepository.SelectColumns casts persona_id::bigint for its own identically-shaped column.
    const string SelectColumns =
        "select persona_id::bigint as persona_id, bytes, byte_size, sha256, token, source, imported_from, updated_at from station.persona_avatar";

    public async Task<PersonaAvatar?> GetByPersonaIdAsync(long personaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<PersonaAvatarRow>(new CommandDefinition(
            $"{SelectColumns} where persona_id = @personaId",
            new { personaId },
            cancellationToken: ct));
        return row is null ? null : ToEntry(row);
    }

    public async Task<PersonaAvatar?> GetByTokenAsync(string token, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<PersonaAvatarRow>(new CommandDefinition(
            $"{SelectColumns} where token = @token",
            new { token },
            cancellationToken: ct));
        return row is null ? null : ToEntry(row);
    }

    /// <summary>
    /// Single-statement upsert (SPEC F129.1): the real <c>UNIQUE(persona_id)</c> constraint is the ON
    /// CONFLICT target, not a pre-check — mirrors <see cref="ThemeRepository.UpsertAsync"/>'s own
    /// insert-or-update-in-one-round-trip shape. Every column, including <c>token</c>, is taken
    /// verbatim from <paramref name="avatar"/> — this store does not generate or rotate the token
    /// itself (see <see cref="IPersonaAvatarStore.UpsertAsync"/>'s own remarks); <c>byte_size</c> is
    /// <paramref name="avatar"/>'s own derived <see cref="PersonaAvatarInput.ByteSize"/>, never a
    /// separately-trusted value (SPEC F129.1 review — <see cref="PersonaAvatarInput"/> makes a
    /// disagreeing size unconstructable in the first place). <c>updated_at</c> is always the write's own
    /// <c>now()</c> — <see cref="PersonaAvatarInput"/> carries no updated-at member for this store to
    /// even consider trusting.
    /// </summary>
    public async Task UpsertAsync(PersonaAvatarInput avatar, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.persona_avatar (persona_id, bytes, byte_size, sha256, token, source, imported_from, updated_at)
            values (@PersonaId, @Bytes, @ByteSize, @Sha256, @Token, @Source, @ImportedFrom, now())
            on conflict (persona_id) do update
              set bytes = @Bytes,
                  byte_size = @ByteSize,
                  sha256 = @Sha256,
                  token = @Token,
                  source = @Source,
                  imported_from = @ImportedFrom,
                  updated_at = now()
            """,
            new
            {
                avatar.PersonaId, avatar.Bytes, avatar.ByteSize, avatar.Sha256, avatar.Token,
                Source = ToSourceText(avatar.Source), avatar.ImportedFrom,
            },
            cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(long personaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona_avatar where persona_id = @personaId",
            new { personaId },
            cancellationToken: ct));
        return affected > 0;
    }

    static PersonaAvatar ToEntry(PersonaAvatarRow row) => new(
        row.PersonaId, row.Bytes, row.ByteSize, row.Sha256, row.Token, ToSource(row.Source), row.ImportedFrom,
        row.UpdatedAt);

    static string ToSourceText(PersonaAvatarSource source) => source switch
    {
        PersonaAvatarSource.Upload => "upload",
        PersonaAvatarSource.Catalog => "catalog",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown persona avatar source"),
    };

    static PersonaAvatarSource ToSource(string source) => source switch
    {
        "upload" => PersonaAvatarSource.Upload,
        "catalog" => PersonaAvatarSource.Catalog,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown persona avatar source"),
    };
}
