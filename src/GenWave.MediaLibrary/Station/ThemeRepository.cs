using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IThemeStore"/> (SPEC F103.7, STORY-271, PLAN T181)
/// over <c>station.theme</c>. Connection-per-query, mirrors <see cref="PersonaRepository"/>'s own
/// station-schema wiring; a single-table upsert never needs <see cref="PersonaImportRepository"/>'s
/// own multi-table transaction.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema
/// store in this file's directory (<see cref="PersonaRepository"/>, <see cref="ScheduleRepository"/>):
/// merely resolving <see cref="IThemeStore"/> from DI must never be enough to trigger a connection
/// attempt against an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class ThemeRepository(Lazy<NpgsqlDataSource> dataSource) : IThemeStore
{
    const string SelectColumns =
        "select slug, definition::text as definition, imported_from, imported_at, created_at from station.theme";

    /// <summary>
    /// Single-statement upsert (SPEC F103.6/F103.7/F104.13): the real <c>UNIQUE(slug)</c> constraint is
    /// the ON CONFLICT target, not a pre-check — mirrors <c>MediaRatingRepository.VoteAsync</c>'s own
    /// insert-or-update-in-one-round-trip shape. <c>imported_at</c> is stamped <c>now()</c> on both the
    /// insert and the update branch whenever <paramref name="importedFrom"/> is non-null — mirroring
    /// <c>PersonaImportRepository.UpsertPersonaAsync</c>'s own "a re-import refreshes the stamp" rule
    /// for <c>station.persona</c> — and left <see langword="null"/> whenever it is (PLAN T207,
    /// <see cref="GenWave.Host.Api.ThemesSaveAsOwnController"/>'s own SPEC F104.13 write, the first
    /// caller ever to pass a null <paramref name="importedFrom"/>): the CASE expression below is what
    /// makes <c>GenWave.Core.Domain.OwnerTheme</c>'s own documented "<c>ImportedAt</c> is
    /// <see langword="null"/> exactly when <c>ImportedFrom</c> is" invariant actually true at the SQL
    /// layer, rather than merely asserted in a doc comment no caller enforced before T207 — an
    /// unconditional <c>now()</c> would have stamped a fabricated "imported at" moment onto an authored
    /// theme that was never imported at all.
    /// </summary>
    public async Task UpsertAsync(string slug, string definition, string? importedFrom, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.theme (slug, definition, imported_from, imported_at)
            values (@Slug, @Definition::jsonb, @ImportedFrom, case when @ImportedFrom is null then null else now() end)
            on conflict (slug) do update
              set definition = @Definition::jsonb,
                  imported_from = @ImportedFrom,
                  imported_at = case when @ImportedFrom is null then null else now() end
            """,
            new { Slug = slug, Definition = definition, ImportedFrom = importedFrom },
            cancellationToken: ct));
    }

    /// <summary>
    /// The save-as-own write's own ATOMIC conditional upsert (SPEC F104.13, PLAN T207 review finding
    /// F2, gh-#394's fix — see this method's own <see cref="IThemeStore.SaveAsOwnAsync"/> contract
    /// remarks for why it is a separate method rather than a branch inside <see cref="UpsertAsync"/>,
    /// and mirrors <c>ShowRepository.ImportAsync</c>'s own conditional-write shape). The <c>WHERE</c>
    /// clause gates the <c>DO UPDATE</c> arm only — a fresh slug always takes the plain <c>INSERT</c>
    /// branch, never touching the clause at all. On a conflict with a row whose own
    /// <c>imported_from</c> is already <see langword="null"/> (re-saving over a theme THIS write path
    /// authored before) the clause evaluates true and the update applies normally. On a conflict with a
    /// row holding real provenance (an IMPORTED theme) the clause evaluates false, so Postgres performs
    /// neither the update nor an insert — zero rows affected, exactly the signal this method turns into
    /// <see langword="false"/>. This closes the exact race a prior read-then-write pair
    /// (<c>ThemesSaveAsOwnController</c>'s own former <c>GetBySlugAsync</c>-then-<c>UpsertAsync</c>)
    /// left open: an import committing between the read and the write could no longer slip past the
    /// guard, because there is no longer a gap between the read and the write for it to slip through.
    /// </summary>
    public async Task<bool> SaveAsOwnAsync(string slug, string definition, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.theme (slug, definition, imported_from, imported_at)
            values (@Slug, @Definition::jsonb, null, null)
            on conflict (slug) do update
              set definition = @Definition::jsonb,
                  imported_from = null,
                  imported_at = null
              where station.theme.imported_from is null
            """,
            new { Slug = slug, Definition = definition },
            cancellationToken: ct));
        return affected == 1;
    }

    public async Task<IReadOnlyList<OwnerTheme>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OwnerTheme>(new CommandDefinition(SelectColumns, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<OwnerTheme?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<OwnerTheme>(new CommandDefinition(
            $"{SelectColumns} where slug = @slug",
            new { slug },
            cancellationToken: ct));
    }
}
