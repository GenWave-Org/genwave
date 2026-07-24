using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IRequestCatalogProbe"/> (SPEC F87.5, STORY-226,
/// PLAN T89) over <c>library.media</c>/<c>library.media_rating</c>. Connection-per-call against the
/// library's own <see cref="NpgsqlDataSource"/>, mirroring <see cref="MediaLibraryMembershipRepository"/>'s
/// wiring — singleton-safe with no captive dependency.
///
/// <para>
/// One query, one round trip. The WHERE clause is the exact <c>GetRandomReadyAsync</c>/
/// <c>RandomSelectionProvider</c> selectability predicate (<c>state = 'ready' and measurable and
/// eligible and not coalesce(never_play, false)</c> — operator vetoes are law, SPEC F87.5; a
/// matched-but-unmeasurable row is not selectable and must not idle a request to expiry) AND'd with an
/// ILIKE contains-match against whichever of <paramref name="artist"/>/<paramref name="title"/> the
/// caller actually supplied. An exact case-insensitive match (<c>lower(col) = lower(@value)</c>) ranks
/// ahead of a mere substring hit; ties break on rating score, then at random — the same
/// "score never enters selection odds beyond a tie-break" posture <c>GetRandomReadyAsync</c> already
/// established (SPEC F33.8).
/// </para>
///
/// <para>
/// <paramref name="artist"/>/<paramref name="title"/> are attacker-controlled (an anonymous spectator
/// wrote the wish this predicate was parsed from) — both are ONLY ever bound as query parameters, and
/// the ILIKE pattern is built by <see cref="EscapeLikeWildcards"/> so a literal <c>%</c> or <c>_</c> in
/// the wish can never behave as a wildcard (e.g. a wish for "100%" matches only the literal
/// substring "100%", never "100" followed by anything).
/// </para>
///
/// <para>
/// gh-#99 — the probe additionally excludes any row whose <c>library_id</c> falls in the live
/// <paramref name="safeScope"/> (re-read fresh on every call, never cached, via <see cref="ISafeScopeProvider"/>
/// — the same idiom <see cref="MediaRatingRepository"/> uses). Safe content (the seeded safe loop,
/// authored safe segments, station IDs) is functional audio, not requestable music: without this
/// carve-out an anonymous wish that happens to title-match a safe row (e.g. "please stand by") would
/// let a listener request a track the operator has structurally no recourse to never-play (gh-#99
/// forbids exactly that write) — a probe hit the operator cannot veto. No-op (no SQL fragment, no
/// parameter) when the safe scope is empty — the pre-#99 behavior.
/// </para>
/// </summary>
sealed class RequestCatalogProbeRepository(NpgsqlDataSource dataSource, ISafeScopeProvider safeScope) : IRequestCatalogProbe
{
    public async Task<long?> FindBestAsync(string? artist, string? title, CancellationToken ct)
    {
        if (artist is null && title is null)
            return null;

        var whereParts = new List<string>
        {
            "m.state = 'ready'",
            "m.measurable",
            "m.eligible",
            "not coalesce(r.never_play, false)",
        };
        var exactParts = new List<string>();
        var parameters = new DynamicParameters();

        if (artist is not null)
        {
            whereParts.Add("m.artist ilike @artistPattern escape '\\'");
            exactParts.Add("lower(m.artist) = lower(@artist)");
            parameters.Add("artistPattern", $"%{EscapeLikeWildcards(artist)}%");
            parameters.Add("artist", artist);
        }

        if (title is not null)
        {
            whereParts.Add("m.title ilike @titlePattern escape '\\'");
            exactParts.Add("lower(m.title) = lower(@title)");
            parameters.Add("titlePattern", $"%{EscapeLikeWildcards(title)}%");
            parameters.Add("title", title);
        }

        var scope = safeScope.Current;
        if (!scope.IsEmpty)
        {
            whereParts.Add("not (m.library_id = any(@safeLibraryIds))");
            parameters.Add("safeLibraryIds", scope.LibraryIds.ToArray());
        }

        var sql = $"""
            select m.id
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            where {string.Join(" and ", whereParts)}
            order by
              case when {string.Join(" and ", exactParts)} then 0 else 1 end,
              r.score desc nulls last,
              random()
            limit 1
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c>, and <c>_</c> (backslash first, so it never double-escapes the
    /// escapes it just inserted) so the caller's literal text survives an ILIKE contains-match
    /// unchanged by Postgres' own pattern-matching wildcards — a wish of <c>a_b</c> matches only the
    /// literal three characters, never <c>aXb</c>.
    /// </summary>
    static string EscapeLikeWildcards(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
