using System.Globalization;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IMediaCatalog"/> (PRD §4.2, SEAM 2) over the
/// <c>library</c> schema. Connection-per-query against the library's own <see cref="NpgsqlDataSource"/>
/// (built from the library_svc connection string), so it is singleton-safe with no captive dependency.
/// A query NEVER triggers a scan — it always hits the already-built catalog (PRD §5).
/// </summary>
sealed class MediaRepository(
    NpgsqlDataSource dataSource,
    ILogger<MediaRepository> logger,
    Channel<long> enrichQueue,
    IStationEventSink? events = null)
    : IMediaCatalog, IAdminMediaLookup, IAdminMediaQuery, IAdminMediaWrite, IAdminMediaReenrichment,
      IAuthoredCatalogWriter
{
    // MediaMutated publish seam for the admin writes (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    const string SelectColumns =
        "select id, path, format, state, title, duration_ms, sample_rate, channels, bitrate_kbps, " +
        "artist, album, genre, year, integrated_lufs, true_peak_dbtp, measurable, " +
        "cue_in_sec, cue_out_sec, intro_energy, outro_energy from library.media";

    // xmin is a Postgres system column; cast to text so Dapper maps it as a plain string. The
    // LEFT JOIN + COALESCE resolves rating state (SPEC F33.10) — an unrated row (no
    // library.media_rating row) reads the F33.2 ledger default (score 50, not flagged) rather
    // than null. Mirrors the join style S3 added to GetRandomReadyAsync/GetStatusCountsAsync.
    // xmin must be table-qualified (m.xmin) once a second real table joins in — media_rating has
    // its own xmin system column too, so the unqualified name is only resolvable pre-join.
    const string SelectColumnsWithLibrary =
        "select id, library_id, path, format, state, title, duration_ms, sample_rate, channels, bitrate_kbps, " +
        "artist, album, genre, year, integrated_lufs, true_peak_dbtp, measurable, " +
        "cue_in_sec, cue_out_sec, intro_energy, outro_energy, bpm, track_energy, eligible, m.xmin::text as xmin, " +
        "coalesce(r.score, 50) as score, coalesce(r.never_play, false) as never_play " +
        "from library.media m left join library.media_rating r on r.media_id = m.id";

    public async Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty) return null;

        // The id is opaque to callers but is our bigint serialized as a string.
        if (!long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<MediaRow>(new CommandDefinition(
            $"{SelectColumns} where id = @id and library_id = any(@libraryIds)",
            new { id, libraryIds = scope.LibraryIds.ToArray() }, cancellationToken: ct));
        return row?.ToReference(logger);
    }

    public async Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty) return null;

        // Only ready + measurable tracks are selectable: playout needs loudness to compute gain, so a
        // not-yet-enriched file simply isn't returned until its metadata lands (PRD §5.2). order by
        // random() is fine at homelab scale (PRD §6). The LEFT JOIN to library.media_rating supplies
        // the never_play flag (default false via COALESCE for an unrated row) so an operator's "X" on
        // this row removes it from rotation immediately (F33.6) — this predicate is the ONE place
        // never_play is enforced for main rotation; score is intentionally never referenced (F33.8).
        var exclude = new List<long>(excludeIds.Count);
        foreach (var s in excludeIds)
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                exclude.Add(v);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<MediaRow>(new CommandDefinition(
            $"{SelectColumns} m " +
            "left join library.media_rating r on r.media_id = m.id " +
            "where state = 'ready' and measurable and eligible and not coalesce(r.never_play, false) " +
            "and id <> all(@exclude) and library_id = any(@libraryIds) " +
            "order by random() limit 1",
            new { exclude, libraryIds = scope.LibraryIds.ToArray() }, cancellationToken: ct));
        return row?.ToReference(logger);
    }

    /// <summary>
    /// SPEC F41.1/F41.3 — one tiered query. The playable predicate is byte-identical to
    /// <see cref="GetRandomReadyAsync"/>'s; the ORDER BY adds two preference tiers ahead of
    /// <c>random()</c>, most-binding first, so a relaxation never becomes a hard exclusion (F41.2/F41.4):
    /// <list type="number">
    /// <item>prefer an id not in <paramref name="orderedRecentIds"/> (<c>repeated_recent</c>);</item>
    /// <item>prefer an artist that does not case-insensitively match any artist among the last
    /// <paramref name="artistSeparation"/> entries of that list, resolved via a correlated subquery over
    /// those ids so no artist state lives in C# (<c>repeated_artist</c>); blank/<c>null</c> artists are
    /// exempt on both sides;</item>
    /// <item>prefer any id over the single most-recent entry (the back-to-back guard, F41.4).</item>
    /// </list>
    /// Both tier outcomes are selected alongside the row so the caller gets its relaxation flags in the
    /// same round trip. Null is returned only when the scope is empty or the playable pool is (F41.2).
    /// </summary>
    public async Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty) return null;

        // orderedRecentIds is oldest-first, most-recent LAST (F41.1); tts:* ids are already stripped by
        // the Orchestrator, but any id that still fails to parse is silently dropped — mirrors
        // GetRandomReadyAsync's exclude-list parsing.
        var recentIds = new List<long>(orderedRecentIds.Count);
        foreach (var s in orderedRecentIds)
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                recentIds.Add(v);

        // Tier 2 only compares against the artists of the LAST artistSeparation entries; a
        // non-positive separation disables the tier by supplying an empty comparison set.
        var recentArtistIds = artistSeparation > 0
            ? recentIds.Skip(Math.Max(0, recentIds.Count - artistSeparation)).ToArray()
            : [];

        // Tier 3 is the single most-recent entry — the last element of the ordered list.
        long? mostRecentId = recentIds.Count > 0 ? recentIds[^1] : null;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RotationCandidateRow>(new CommandDefinition("""
            select
              m.id, m.path, m.format, m.state, m.title, m.duration_ms, m.sample_rate, m.channels,
              m.bitrate_kbps, m.artist, m.album, m.genre, m.year, m.integrated_lufs, m.true_peak_dbtp,
              m.measurable, m.cue_in_sec, m.cue_out_sec, m.intro_energy, m.outro_energy,
              (m.id = any(@recentIds)) as repeated_recent,
              (
                m.artist is not null and trim(m.artist) <> ''
                and exists (
                  select 1 from library.media rm
                  where rm.id = any(@recentArtistIds)
                    and rm.artist is not null and trim(rm.artist) <> ''
                    and lower(trim(rm.artist)) = lower(trim(m.artist))
                )
              ) as repeated_artist
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            where m.state = 'ready' and m.measurable and m.eligible and not coalesce(r.never_play, false)
              and m.library_id = any(@libraryIds)
            order by
              repeated_recent asc,
              repeated_artist asc,
              coalesce(m.id = @mostRecentId, false) asc,
              random()
            limit 1
            """,
            new { recentIds, recentArtistIds, mostRecentId, libraryIds = scope.LibraryIds.ToArray() },
            cancellationToken: ct));

        return row?.ToCandidate(logger);
    }

    public async Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty)
            return new PagedResult<MediaReference>([], 0, 0);

        // Hard ceiling — callers should already clamp, but defense-in-depth.
        var limit = Math.Clamp(query.Limit, 1, 200);
        var page  = Math.Max(1, query.Page);
        var offset = (page - 1) * limit;

        // Build the WHERE clause dynamically using a flag list so each predicate is optional
        // but the base predicate (library_id scope) is always present.
        var whereParts = new List<string> { "library_id = any(@libraryIds)" };

        if (query.State is not null)    whereParts.Add("state = @state");
        if (query.Artist is not null)   whereParts.Add("artist ILIKE @artist");
        if (query.Genre is not null)    whereParts.Add("genre ILIKE @genre");
        // LibraryId narrowing is already reflected in the scope by the controller; keeping
        // it here as a belt-and-suspenders exact match so the SQL mirrors the intent.
        if (query.LibraryId.HasValue)   whereParts.Add("library_id = @libraryId");
        if (query.Q is not null)        whereParts.Add("(title ILIKE @q OR artist ILIKE @q OR album ILIKE @q)");

        var where = string.Join(" and ", whereParts);

        // COUNT(*) OVER() gives us the total in a single round-trip (Postgres window function).
        var sql = $"""
            select id, path, title, duration_ms, sample_rate, channels, bitrate_kbps,
                   artist, album, genre, year, integrated_lufs, true_peak_dbtp, measurable,
                   cue_in_sec, cue_out_sec, intro_energy, outro_energy, count(*) over() as total_count
            from library.media
            where {where}
            order by id
            offset @offset limit @limit
            """;

        var parameters = new
        {
            libraryIds = scope.LibraryIds.ToArray(),
            state      = query.State,
            artist     = query.Artist is not null ? $"%{query.Artist}%" : null,
            genre      = query.Genre  is not null ? $"%{query.Genre}%"  : null,
            libraryId  = query.LibraryId,
            q          = query.Q is not null ? $"%{query.Q}%" : null,
            offset,
            limit,
        };

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MediaRowWithCount>(new CommandDefinition(sql, parameters, cancellationToken: ct));

        var list = rows.AsList();
        if (list.Count == 0)
            return new PagedResult<MediaReference>([], 0, 0);

        var total = list[0].TotalCount;
        var pages = (int)Math.Ceiling((double)total / limit);
        var items = list.Select(r => r.ToReference(logger)).ToList();
        return new PagedResult<MediaReference>(items, total, pages);
    }

    /// <summary>
    /// The <c>GET /api/status</c> aggregate's catalog counts (SPEC F28.6) — one grouped query.
    /// <c>count(*) filter (where ...)</c> lets a single table scan produce all five numbers: the
    /// four state counts (unscoped, mirroring <c>GET /api/libraries</c>, F20.1) plus
    /// <c>playable</c>, which repeats <see cref="GetRandomReadyAsync"/>'s exact
    /// <c>state = 'ready' and measurable and eligible and not coalesce(r.never_play, false)</c>
    /// predicate scoped to <paramref name="safeScope"/> so it agrees with what
    /// <c>/internal/safe-track</c> would serve — including the F33.6 never-play exclusion, so an
    /// all-flagged SafeScope truthfully reports 0 and the F31.4/F31.5 depleted warnings fire.
    /// An empty <paramref name="safeScope"/> needs no special case: <c>library_id = any('{}')</c>
    /// matches nothing, so <c>playable</c> is naturally 0 (default-deny).
    /// <c>discovered</c> is presented as <c>enriching</c> — the operator-facing label for "awaiting
    /// or mid-enrichment" (there is no separate in-progress state; SPEC F28.6).
    /// </summary>
    public async Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<CatalogStatusCounts>(new CommandDefinition("""
            select
              count(*) filter (where state = 'ready')::int        as ready,
              count(*) filter (where state = 'discovered')::int   as enriching,
              count(*) filter (where state = 'failed')::int       as failed,
              count(*) filter (where state = 'unavailable')::int  as unavailable,
              count(*) filter (
                where state = 'ready' and measurable and eligible
                  and not coalesce(r.never_play, false)
                  and library_id = any(@libraryIds)
              )::int as playable
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            """,
            new { libraryIds = safeScope.LibraryIds.ToArray() }, cancellationToken: ct));
    }

    /// <summary>
    /// SPEC F52.1/F52.2 (closes gitea-#189) — distinct, non-NULL, non-blank values of <paramref name="field"/>'s
    /// backing column, grouped case-insensitively via <c>group by lower(column)</c> so "Rock"/"rock"
    /// collapse into one entry with the group's total row count (never two divided-count rows).
    /// <c>min(column)</c> picks the representative original casing — deterministic (a total order over
    /// the group's exact string values, unlike <c>mode()</c>'s implementation-defined tie-break) and
    /// simpler than an ordered-set aggregate for a facet list nobody needs "most common casing" from.
    /// Ordered by the same <c>lower(column)</c> grouping key, so ordering and grouping never disagree.
    /// Scope predicate is identical to <see cref="ListAdminAsync"/>'s (<c>library_id = any(@libraryIds)</c>);
    /// an empty scope short-circuits to an empty list without touching the database (default-deny).
    /// </summary>
    public async Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty) return [];

        // The column name is drawn from a compile-time-fixed switch over a closed enum — never user
        // input — so interpolating it into the SQL string carries no injection surface.
        var column = field switch
        {
            FacetField.Artist => "artist",
            FacetField.Album  => "album",
            FacetField.Genre  => "genre",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown facet field."),
        };

        var sql = $"""
            select min({column}) as value, count(*)::int as count
            from library.media
            where library_id = any(@libraryIds)
              and {column} is not null
              and trim({column}) <> ''
            group by lower({column})
            order by lower({column})
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<FacetValue>(new CommandDefinition(
            sql, new { libraryIds = scope.LibraryIds.ToArray() }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<MediaRowWithLibrary>(new CommandDefinition(
            $"{SelectColumnsWithLibrary} where id = @id",
            new { id }, cancellationToken: ct));

        if (row is null)
            return null;

        return (row.ToAdminDto(), row.LibraryId);
    }

    public async Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        // Default-deny: no scope means no access, no SQL issued.
        if (scope.IsEmpty)
            return new PagedResult<AdminMediaDto>([], 0, 0);

        var limit = Math.Clamp(query.Limit, 1, 200);
        var page  = Math.Max(1, query.Page);
        var offset = (page - 1) * limit;

        var (where, filterParams) = BuildAdminWhere(query, scope);

        // The never-play filter is browse-only (SPEC F33.10) and deliberately NOT folded into
        // BuildAdminWhere: that helper's WHERE fragment is also used by SetEligibilityAsync,
        // BulkReassignAsync, and ScheduleBulkAsync, which UPDATE library.media directly with no
        // join to media_rating — an `r.never_play` predicate there would reference an undefined
        // alias. See MediaQuery.NeverPlay's doc for the true/absent/false semantics.
        if (query.NeverPlay is true)
            where += " and coalesce(r.never_play, false)";

        filterParams.Add("offset", offset);
        filterParams.Add("limit", limit);

        var sql = $"""
            select id, path, format, state, title, duration_ms, sample_rate, channels, bitrate_kbps,
                   artist, album, genre, year, integrated_lufs, true_peak_dbtp, measurable,
                   cue_in_sec, cue_out_sec, intro_energy, outro_energy, bpm, track_energy,
                   eligible, m.xmin::text as xmin,
                   coalesce(r.score, 50) as score, coalesce(r.never_play, false) as never_play,
                   count(*) over() as total_count
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            where {where}
            order by id
            offset @offset limit @limit
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MediaRowWithCount>(new CommandDefinition(sql, filterParams, cancellationToken: ct));

        var list = rows.AsList();
        if (list.Count == 0)
            return new PagedResult<AdminMediaDto>([], 0, 0);

        var total = list[0].TotalCount;
        var pages = (int)Math.Ceiling((double)total / limit);
        var items = list.Select(r => r.ToAdminDto()).ToList();
        return new PagedResult<AdminMediaDto>(items, total, pages);
    }

    // ── Shared admin WHERE builder ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the shared admin WHERE fragment and its Dapper parameters for a <see cref="MediaQuery"/>.
    /// <see cref="ListAdminAsync"/>, <see cref="SetEligibilityAsync"/>, <see cref="BulkReassignAsync"/>,
    /// and — cross-class — <see cref="MediaRatingRepository"/>'s bulk vote/never-play writes (SPEC F61,
    /// STORY-158) all call this so the predicate set exists exactly once — a future column addition
    /// only needs to be made here. Internal (not private) for that cross-class reuse: both types live
    /// in this project's <c>Catalog</c> namespace, and the WHERE fragment is pure — no state, no
    /// connection — so sharing it carries none of a shared-mutable-state coupling risk.
    ///
    /// Parameter naming note: the eligible FILTER predicate uses <c>@filterEligible</c> so callers
    /// that also need an <c>@eligible</c> SET parameter (e.g. <see cref="SetEligibilityAsync"/>)
    /// face no collision.
    ///
    /// The scope predicate (<c>library_id = any(@libraryIds)</c>) is always present and always first.
    ///
    /// <c>Year</c>/<c>Decade</c>/<c>YearMissing</c> (SPEC F49.1) are plain <c>library.media</c>
    /// column predicates — unlike <see cref="MediaQuery.NeverPlay"/> they need no join to
    /// <c>library.media_rating</c>, so they are folded in here and reach the bulk write paths too.
    /// The controller rejects (400) naming more than one of the three before a <see cref="MediaQuery"/>
    /// carrying more than one is ever built; this helper trusts that has already happened and applies
    /// whichever are set. <c>Decade</c> expands to <c>year BETWEEN Decade AND Decade+9</c> — decade
    /// alignment is validated by the controller, not here.
    ///
    /// <c>ArtistExact</c>/<c>AlbumExact</c>/<c>GenresExact</c> (SPEC F52.3/F52.4) are additive
    /// case-insensitive equality predicates (<c>lower(col) = lower(@value)</c>) landing here exactly
    /// once so <see cref="ListAdminAsync"/> and every bulk write path agree on the row set. Genres
    /// OR-match via <c>lower(genre) = any(@genresExact)</c> against a pre-lowercased array — a null or
    /// empty <c>GenresExact</c> applies no filter.
    ///
    /// Blank/whitespace-only exact values are treated as absent, not as a real (and unmatchable)
    /// equality target: a native GET form serializes every sibling filter input, so the Catalog
    /// bulk-toolbar's request body can carry <c>"albumExact":""</c> even when the operator only
    /// picked an artist — <c>lower(album) = lower('')</c> would then match nothing and zero out an
    /// otherwise-correct sweep (SPEC F52.4 smoke defect, live repro: <c>artist-exact=Queen</c> with
    /// an empty sibling <c>album-exact</c> field affected 0 of 2 matching rows). This is the single
    /// implementation point every browse + bulk write path shares, so ignoring a blank here keeps
    /// browse and bulk symmetric everywhere at once; a blank exact match is operator-meaningless
    /// regardless of layer, so ignoring it is least-astonishment. <c>GenresExact</c> mirrors this by
    /// dropping blank entries before the count check, so a list of only blanks (e.g. <c>[""]</c>)
    /// also applies no filter.
    /// </summary>
    internal static (string Where, DynamicParameters Params) BuildAdminWhere(MediaQuery query, LibraryScope scope)
    {
        var parts = new List<string> { "library_id = any(@libraryIds)" };

        var artistExact = string.IsNullOrWhiteSpace(query.ArtistExact) ? null : query.ArtistExact;
        var albumExact  = string.IsNullOrWhiteSpace(query.AlbumExact)  ? null : query.AlbumExact;
        var genresExact = query.GenresExact?.Where(g => !string.IsNullOrWhiteSpace(g)).ToArray();
        if (genresExact is { Length: 0 }) genresExact = null;

        if (query.State is not null)    parts.Add("state = @state");
        if (query.Artist is not null)   parts.Add("artist ILIKE @artist");
        if (query.Genre is not null)    parts.Add("genre ILIKE @genre");
        if (query.LibraryId.HasValue)   parts.Add("library_id = @libraryId");
        if (query.Q is not null)        parts.Add("(title ILIKE @q OR artist ILIKE @q OR album ILIKE @q)");
        if (query.Eligible.HasValue)    parts.Add("eligible = @filterEligible");
        if (query.Year.HasValue)        parts.Add("year = @year");
        if (query.Decade.HasValue)      parts.Add("year between @decadeStart and @decadeEnd");
        if (query.YearMissing is true)  parts.Add("year is null");
        if (artistExact is not null)    parts.Add("lower(artist) = lower(@artistExact)");
        if (albumExact is not null)     parts.Add("lower(album) = lower(@albumExact)");
        if (genresExact is not null)    parts.Add("lower(genre) = any(@genresExact)");

        var p = new DynamicParameters();
        p.Add("libraryIds", scope.LibraryIds.ToArray());
        p.Add("state",      query.State);
        p.Add("artist",     query.Artist is not null ? $"%{query.Artist}%" : null);
        p.Add("genre",      query.Genre  is not null ? $"%{query.Genre}%"  : null);
        p.Add("libraryId",  query.LibraryId);
        p.Add("q",          query.Q is not null ? $"%{query.Q}%" : null);
        p.Add("filterEligible", query.Eligible);
        p.Add("year",        query.Year);
        p.Add("decadeStart", query.Decade);
        p.Add("decadeEnd",   query.Decade.HasValue ? query.Decade.Value + 9 : null);
        p.Add("artistExact", artistExact);
        p.Add("albumExact",  albumExact);
        p.Add("genresExact", genresExact?.Select(g => g.ToLowerInvariant()).ToArray());

        return (string.Join(" and ", parts), p);
    }

    // Postgres SQLSTATE code for foreign-key violation — mirrors AdminLibraryRepository.
    const string ForeignKeyViolation = "23503";

    // ── Admin catalog write (IAdminMediaWrite — W2) ──────────────────────────────────────────────────

    /// <summary>
    /// Sparse patch: only non-null fields are written. When any tag field (title/artist/album/genre/year)
    /// is set, <c>tags_edited_at = now()</c> is also written — the W3 sentinel that prevents
    /// re-enrichment from clobbering operator edits.
    ///
    /// <c>library_id</c> reassignment (STORY-048): when <c>patch.LibraryId</c> is set, the row is
    /// moved to that library. If the destination library does not exist, Postgres raises a
    /// foreign-key violation (23503) which is caught and mapped to
    /// <see cref="MediaWriteResult.UnknownLibraryId"/> — no application-level pre-flight SELECT is
    /// performed (common path is one round-trip; the count query only fires on violation, mirroring
    /// the AdminLibraryRepository pattern).
    ///
    /// Concurrency: the UPDATE WHERE clause includes <c>xmin = @expected::xid</c>. Scope is never part
    /// of the WHERE clause (SPEC F43.2 — scope is a curation filter, not an access gate). If 0 rows
    /// are affected, a follow-up SELECT distinguishes:
    ///   • row absent entirely → <see cref="MediaWriteResult.NotFound"/> (checked first — IDOR-safe)
    ///   • row present → version mismatch → <see cref="MediaWriteResult.Conflict"/>
    ///
    /// Delegates to <see cref="UpdateCoreAsync"/> and discards the returned version — kept as a
    /// distinct public method (rather than folded into <see cref="UpdateReturningVersionAsync"/>)
    /// so STORY-039's pinned interface contract is undisturbed.
    /// </summary>
    public async Task<MediaWriteResult> UpdateAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
    {
        var (result, _, _) = await UpdateCoreAsync(id, patch, expectedVersion, ct);
        if (result == MediaWriteResult.Updated)
            events.Publish(new MediaMutated("patch", ParseMediaId(id), 1));
        return result;
    }

    /// <summary>
    /// Same write as <see cref="UpdateAsync"/>, additionally returning the row's new <c>xmin</c>
    /// token and current <c>library_id</c> straight from the UPDATE's <c>RETURNING</c> clause
    /// (STORY-103; <c>LibraryId</c> added by SPEC F43.2) — no second read.
    /// </summary>
    public async Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
    {
        var (result, newVersion, libraryId) = await UpdateCoreAsync(id, patch, expectedVersion, ct);
        if (result == MediaWriteResult.Updated)
            events.Publish(new MediaMutated("patch", ParseMediaId(id), 1));
        return new MediaUpdateOutcome(result, newVersion, libraryId);
    }

    /// <summary>Best-effort id parse for event payloads only — write paths do their own parsing.</summary>
    static long? ParseMediaId(string id) =>
        long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>
    /// Shared implementation behind <see cref="UpdateAsync"/> and
    /// <see cref="UpdateReturningVersionAsync"/> — the SQL and disambiguation logic exist exactly
    /// once. Returns the row's post-write <c>xmin</c> and current <c>library_id</c> alongside the
    /// outcome; non-<c>Updated</c> outcomes always carry <c>null</c> for both. The library id lets
    /// endpoint-layer callers (Host project) add the <c>X-Out-Of-Scope</c> warning without a second
    /// read when the patch did not itself request a library reassignment (SPEC F43.2).
    ///
    /// No <see cref="LibraryScope"/> parameter — SPEC F43.2 repeals the source-row scope gate on
    /// this path, so the write reaches any existing row by id regardless of its current library.
    /// (The destination-out-of-scope warning on reassignment, F20.6, is unrelated and lives at the
    /// endpoint layer, which still has the caller's scope via a separate seam.)
    /// </summary>
    async Task<(MediaWriteResult Result, string? NewVersion, long? LibraryId)> UpdateCoreAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        CancellationToken ct)
    {
        if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowId))
            return (MediaWriteResult.NotFound, null, null);

        // Build the SET clause from the non-null patch fields only.
        var setClauses = new List<string>();
        bool anyTagField = patch.Title is not null || patch.Artist is not null ||
                           patch.Album is not null || patch.Genre is not null ||
                           patch.Year.HasValue;

        if (patch.Title is not null)     setClauses.Add("title = @title");
        if (patch.Artist is not null)    setClauses.Add("artist = @artist");
        if (patch.Album is not null)     setClauses.Add("album = @album");
        if (patch.Genre is not null)     setClauses.Add("genre = @genre");
        if (patch.Year.HasValue)         setClauses.Add("year = @year");
        if (patch.Eligible.HasValue)     setClauses.Add("eligible = @eligible");
        if (patch.LibraryId.HasValue)    setClauses.Add("library_id = @libraryId");
        // tag sentinel: stamp tags_edited_at only for tag-column changes, not for library moves.
        if (anyTagField)                 setClauses.Add("tags_edited_at = now()");

        // A no-op patch (all nulls) is a valid 200 — no columns changed but the row must still
        // exist. The current xmin/library_id are read in this same SELECT (not a follow-up read
        // after a write, since no write happens on this path) so the no-op case can still carry a
        // fresh version and the scope-warning signal.
        if (setClauses.Count == 0)
        {
            await using var checkConn = await dataSource.OpenConnectionAsync(ct);
            var checkRow = await checkConn.QuerySingleOrDefaultAsync<(long id, string xmin, long libraryId)?>(
                new CommandDefinition(
                    "select id, xmin::text as xmin, library_id from library.media where id = @rowId",
                    new { rowId }, cancellationToken: ct));

            if (checkRow is null) return (MediaWriteResult.NotFound, null, null);
            return (MediaWriteResult.Updated, checkRow.Value.xmin, checkRow.Value.libraryId);
        }

        var setClause = string.Join(", ", setClauses);

        // xmin is a Postgres system column of type `xid` — cast the string token back to xid.
        // No scope predicate (SPEC F43.2). RETURNING the post-write xmin + library_id lets the
        // caller echo a fresh ETag and the scope-warning signal without a second round trip.
        var sql = $"""
            update library.media
            set {setClause}
            where id = @rowId
              and xmin = @expectedVersion::xid
            returning xmin::text, library_id
            """;

        var parameters = new
        {
            rowId,
            expectedVersion,
            title     = patch.Title,
            artist    = patch.Artist,
            album     = patch.Album,
            genre     = patch.Genre,
            year      = patch.Year,
            eligible  = patch.Eligible,
            libraryId = patch.LibraryId,
        };

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        (string xmin, long libraryId)? updated;
        try
        {
            updated = await conn.QuerySingleOrDefaultAsync<(string xmin, long libraryId)?>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            // The destination library_id violates the FK on library.media.library_id — no such library.
            return (MediaWriteResult.UnknownLibraryId, null, null);
        }

        if (updated is not null) return (MediaWriteResult.Updated, updated.Value.xmin, updated.Value.libraryId);

        // 0 rows — disambiguate: is the row absent, or was it a version mismatch? Existence is
        // checked first — IDOR-safe: an unknown id always reports 404, never a signal that would
        // let a caller distinguish "wrong version" from "doesn't exist" (F43.2 preserves this
        // ordering even though scope no longer participates).
        var existing = await conn.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "select id from library.media where id = @rowId",
                new { rowId }, cancellationToken: ct));

        if (existing is null) return (MediaWriteResult.NotFound, null, null);

        // Row exists — the xmin didn't match.
        return (MediaWriteResult.Conflict, null, null);
    }

    /// <summary>
    /// Bulk-sets <c>eligible</c> on every row matching <paramref name="filter"/> within
    /// <paramref name="scope"/>. Uses <see cref="BuildAdminWhere"/> so the predicate set is
    /// identical to <see cref="ListAdminAsync"/> — the operator sees the same rows via GET
    /// before committing the bulk change via POST.
    ///
    /// Scope-bound by <c>library_id = ANY(@libraryIds)</c> — always the first predicate.
    /// Empty scope → 0, no SQL. All filter values are Npgsql parameters; no interpolation.
    /// </summary>
    public async Task<int> SetEligibilityAsync(
        MediaQuery filter,
        bool eligible,
        LibraryScope scope,
        CancellationToken ct)
    {
        // Default-deny: never touch a single row without a scope.
        if (scope.IsEmpty) return 0;

        // BuildAdminWhere uses @filterEligible for the eligible filter predicate so it cannot
        // collide with @eligible (the SET value written here).
        var (where, filterParams) = BuildAdminWhere(filter, scope);
        filterParams.Add("eligible", eligible);

        var sql = $"""
            update library.media
            set eligible = @eligible
            where {where}
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, filterParams, cancellationToken: ct));
        if (affected > 0)
            events.Publish(new MediaMutated("eligibility-bulk", null, affected));
        return affected;
    }

    /// <summary>
    /// Bulk-reassigns every row matching <paramref name="filter"/> within <paramref name="scope"/>
    /// to <paramref name="toLibraryId"/>. Uses <see cref="BuildAdminWhere"/> so the predicate set
    /// is identical to <see cref="ListAdminAsync"/>.
    ///
    /// Pre-validates <paramref name="toLibraryId"/> against <c>library.library</c> before issuing
    /// any UPDATE — a SELECT EXISTS fires first so an unknown destination returns <c>null</c> (→ 400)
    /// even when the filter matches zero rows (an FK violation on an empty match would never fire).
    /// The scope guard is applied AFTER the library check to keep consistent 400 semantics.
    ///
    /// Scope-bound by <c>library_id = ANY(@libraryIds)</c> — always the first WHERE predicate.
    /// Empty scope → 0, no UPDATE issued. All values are Npgsql parameters; no interpolation.
    /// </summary>
    public async Task<int?> BulkReassignAsync(
        MediaQuery filter,
        long toLibraryId,
        LibraryScope scope,
        CancellationToken ct)
    {
        // Open one connection for both the pre-validation SELECT and the UPDATE — avoids a
        // second round-trip and keeps the two operations on a shared connection lifetime.
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Pre-validate destination library. Must run BEFORE the scope guard so that an unknown
        // toLibraryId always returns null (→ 400), even when scope is empty or filter matches nothing.
        var libraryExists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from library.library where id = @toLibraryId)",
            new { toLibraryId },
            cancellationToken: ct));

        if (!libraryExists)
            return null;

        // Default-deny: no scope = no rows to reassign, but destination was valid.
        if (scope.IsEmpty) return 0;

        // BuildAdminWhere builds the WHERE fragment and Dapper DynamicParameters for the scope +
        // filter predicates. The @toLibraryId SET parameter is distinct from any filter param names.
        var (where, filterParams) = BuildAdminWhere(filter, scope);
        filterParams.Add("toLibraryId", toLibraryId);

        var sql = $"""
            update library.media
            set library_id = @toLibraryId
            where {where}
            """;

        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, filterParams, cancellationToken: ct));
        if (affected > 0)
            events.Publish(new MediaMutated("reassign-bulk", null, affected));
        return affected;
    }

    // ── Write side, used by discovery + enrichment within the library (not part of IMediaCatalog). ──

    /// <summary>Every catalog row's change-detection fingerprint, for the discovery delta pass.</summary>
    public async Task<IReadOnlyList<MediaFingerprint>> ListFingerprintsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MediaFingerprint>(new CommandDefinition(
            "select id, path, size_bytes, mtime, state from library.media", cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Insert a newly discovered file (idempotent on path), returning its id. State defaults
    /// to <c>discovered</c>; a re-discovery of a known path resets it so it is re-enriched.</summary>
    public async Task<long> InsertDiscoveredAsync(string path, string format, long sizeBytes, DateTime mtime, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            insert into library.media (path, format, size_bytes, mtime)
            values (@path, @format, @sizeBytes, @mtime)
            on conflict (path) do update set
              format = excluded.format, size_bytes = excluded.size_bytes,
              mtime = excluded.mtime, state = 'discovered'
            returning id
            """, new { path, format, sizeBytes, mtime }, cancellationToken: ct));
    }

    /// <summary>A changed file (size/mtime differs): reset to <c>discovered</c> for re-enrichment.</summary>
    public async Task MarkDiscoveredAsync(long id, long sizeBytes, DateTime mtime, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set size_bytes = @sizeBytes, mtime = @mtime, state = 'discovered' where id = @id",
            new { id, sizeBytes, mtime }, cancellationToken: ct));
    }

    /// <summary>Files gone from disk: mark unavailable — never hard-delete (could be a transient mount
    /// issue); reconcile/purge on a later policy (PRD §5.1).</summary>
    public async Task MarkUnavailableAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set state = 'unavailable' where id = any(@ids)",
            new { ids = ids.ToArray() }, cancellationToken: ct));
    }

    /// <summary>The path to enrich for a media id, or null if the row has since vanished.</summary>
    public async Task<string?> GetPathAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select path from library.media where id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// Ids of rows still awaiting enrichment (<c>discovered</c>) — the durable work queue.
    /// <see cref="Enrich.EnrichmentService"/> uses this on startup to recover pending work: the in-memory
    /// delta channel does not survive a restart, and discovery only re-enqueues disk deltas, so without
    /// this a backlog interrupted by a crash/redeploy would be orphaned in <c>discovered</c> forever —
    /// never enriched, never selectable (PRD §5.2).
    /// </summary>
    public async Task<IReadOnlyList<long>> ListPendingEnrichmentAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<long>(new CommandDefinition(
            "select id from library.media where state = 'discovered'", cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Ids of ready rows where cue_analyzed_at IS NULL — backfill candidates.</summary>
    public async Task<IReadOnlyList<long>> ListBackfillCueAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<long>(new CommandDefinition(
            "select id from library.media where state = 'ready' and cue_analyzed_at is null limit @limit",
            new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Writes backfill cue result for one row; sets cue_analyzed_at unconditionally.</summary>
    public async Task WriteCueBackfillAsync(long id, CuePoints? cue, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set cue_in_sec = @cueInSec, cue_out_sec = @cueOutSec, " +
            "cue_analyzed_at = now() where id = @id",
            new { id, cueInSec = cue?.CueInSec, cueOutSec = cue?.CueOutSec },
            cancellationToken: ct));
    }

    /// <summary>
    /// Rows eligible for energy backfill: <c>state='ready'</c> with <c>energy_analyzed_at IS NULL</c>.
    /// Returns id, path, and existing cue points so the energy analyzer receives the cue-trimmed windows
    /// without a second round-trip. Limited to <paramref name="limit"/> rows per tick.
    /// </summary>
    public async Task<IReadOnlyList<EnergyClaimRow>> ListEnergyClaimsAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<EnergyClaimRow>(new CommandDefinition(
            "select id, path, cue_in_sec, cue_out_sec from library.media " +
            "where state = 'ready' and energy_analyzed_at is null limit @limit",
            new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Writes energy analysis result for a claimed row; sets <c>energy_analyzed_at</c> unconditionally.
    /// Only the energy columns are updated — <c>integrated_lufs</c> and all other columns are untouched.
    /// </summary>
    public async Task WriteEnergyClaimAsync(long id, EnergyPoints? energy, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set intro_energy = @introEnergy, outro_energy = @outroEnergy, " +
            "energy_analyzed_at = now() where id = @id",
            new { id, introEnergy = energy?.IntroEnergy, outroEnergy = energy?.OutroEnergy },
            cancellationToken: ct));
    }

    /// <summary>
    /// Rows eligible for BPM backfill: <c>state='ready'</c> with <c>bpm_analyzed_at IS NULL</c>.
    /// Returns id, path, and existing cue points so the BPM analyzer receives the cue-trimmed windows
    /// without a second round-trip. Limited to <paramref name="limit"/> rows per tick (SPEC F46.3).
    /// Mirrors <see cref="ListEnergyClaimsAsync"/> exactly.
    /// </summary>
    public async Task<IReadOnlyList<BpmClaimRow>> ListBpmClaimsAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<BpmClaimRow>(new CommandDefinition(
            "select id, path, cue_in_sec, cue_out_sec from library.media " +
            "where state = 'ready' and bpm_analyzed_at is null limit @limit",
            new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Writes BPM analysis result for a claimed row; sets <c>bpm_analyzed_at</c> unconditionally —
    /// even when <paramref name="bpm"/> is <c>null</c> (indeterminate tempo), so the backfill
    /// predicate never re-claims this row (SPEC F46.1/F46.3). Only the <c>bpm</c> column is
    /// updated — all other columns are untouched.
    /// </summary>
    public async Task WriteBpmClaimAsync(long id, double? bpm, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set bpm = @bpm, bpm_analyzed_at = now() where id = @id",
            new { id, bpm },
            cancellationToken: ct));
    }

    /// <summary>
    /// Rows eligible for a MusicBrainz year lookup: <c>state='ready'</c>, <c>year IS NULL</c>,
    /// <c>year_lookup_at IS NULL</c>, and both artist and title are non-blank (SPEC F48.3) — a blank
    /// tag has nothing to search MusicBrainz with, so it is never claimed (and, unlike the other
    /// backfills, never gets its sentinel stamped either — it simply isn't selected by this query,
    /// pending an operator tag fix). Limited to <paramref name="limit"/> rows per tick.
    /// </summary>
    public async Task<IReadOnlyList<YearLookupClaimRow>> ListYearLookupClaimsAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<YearLookupClaimRow>(new CommandDefinition(
            "select id, artist, title, album from library.media " +
            "where state = 'ready' and year is null and year_lookup_at is null " +
            "and coalesce(trim(artist), '') <> '' and coalesce(trim(title), '') <> '' " +
            "limit @limit",
            new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Writes the outcome of one MusicBrainz year lookup attempt; <c>year_lookup_at</c> is stamped
    /// unconditionally — success, low confidence, or failure — so the backfill predicate never
    /// re-claims this row (SPEC F48.3). <c>year</c> is written ONLY when the column is currently
    /// <c>null</c> — a <c>CASE</c> guard in the UPDATE itself, not a pre-read, so a concurrent write
    /// (e.g. an operator's PATCH landing between the claim and this write) can never be clobbered by
    /// a stale lookup result (SPEC F48.4). No other column, including <c>tags_edited_at</c>, is
    /// touched — this is an enrichment pass, never an operator edit.
    /// </summary>
    public async Task WriteYearLookupResultAsync(long id, int? year, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set " +
            "year = case when year is null then @year else year end, " +
            "year_lookup_at = now() " +
            "where id = @id",
            new { id, year },
            cancellationToken: ct));
    }

    /// <summary>
    /// Atomic, idempotent write of one enrichment pass; the row becomes <c>ready</c>.
    ///
    /// Tag columns (title/artist/album/album_artist/genre/track_no/year) are written ONLY when
    /// <c>tags_edited_at IS NULL</c> — i.e. the operator has not yet manually edited the row (W3
    /// sentinel). Loudness, cue, energy, and BPM columns are always (re)written; they are disjoint
    /// from the tag columns and operator edits never touch them.
    /// </summary>
    public async Task WriteEnrichmentAsync(long id, EnrichmentResult r, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            update library.media set
              duration_ms  = @DurationMs,
              sample_rate  = @SampleRate,
              channels     = @Channels,
              bitrate_kbps = @BitrateKbps,
              -- Tag columns: only overwrite when the operator has never edited them.
              -- tags_edited_at IS NULL means "never manually edited" → safe to apply file tags.
              -- Once an operator edits (tags_edited_at IS NOT NULL), file tags are ignored forever.
              title        = case when tags_edited_at is null then @Title        else title        end,
              artist       = case when tags_edited_at is null then @Artist       else artist       end,
              album        = case when tags_edited_at is null then @Album        else album        end,
              album_artist = case when tags_edited_at is null then @AlbumArtist  else album_artist end,
              genre        = case when tags_edited_at is null then @Genre        else genre        end,
              track_no     = case when tags_edited_at is null then @TrackNo      else track_no     end,
              year         = case when tags_edited_at is null then @Year         else year         end,
              -- Loudness / cue / energy / bpm: always unconditional — enricher-owned, never operator-edited.
              integrated_lufs    = @IntegratedLufs,
              true_peak_dbtp     = @TruePeakDbtp,
              measurable         = @Measurable,
              cue_in_sec         = @CueInSec,
              cue_out_sec        = @CueOutSec,
              cue_analyzed_at    = @CueAnalyzedAt,
              intro_energy       = @IntroEnergy,
              outro_energy       = @OutroEnergy,
              energy_analyzed_at = @EnergyAnalyzedAt,
              bpm                = @Bpm,
              bpm_analyzed_at    = @BpmAnalyzedAt,
              state       = 'ready',
              enriched_at = now()
            where id = @id
            """,
            new
            {
                id,
                r.DurationMs, r.SampleRate, r.Channels, r.BitrateKbps,
                r.Title, r.Artist, r.Album, r.AlbumArtist, r.Genre, r.TrackNo, r.Year,
                r.IntegratedLufs, r.TruePeakDbtp, r.Measurable,
                r.CueInSec, r.CueOutSec, r.CueAnalyzedAt,
                r.IntroEnergy, r.OutroEnergy, r.EnergyAnalyzedAt,
                r.Bpm, r.BpmAnalyzedAt,
            },
            cancellationToken: ct));
    }

    /// <summary>A per-file enrichment failure: isolated, never crashes the worker (PRD §5.2).</summary>
    public async Task MarkFailedAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update library.media set state = 'failed', enriched_at = now() where id = @id",
            new { id }, cancellationToken: ct));
    }

    // ── Authored insert (IAuthoredCatalogWriter — F27.1/F27.2/F27.8, STORY-076) ──────────────────

    /// <summary>
    /// Lands <paramref name="insert"/> directly in <c>state='ready'</c> — one INSERT, no enricher
    /// round-trip. <c>tags_edited_at</c> and all FOUR <c>*_analyzed_at</c>/lookup sentinels
    /// (<c>cue_analyzed_at</c>, <c>energy_analyzed_at</c>, <c>bpm_analyzed_at</c>, <c>year_lookup_at</c>)
    /// are stamped unconditionally in the same statement:
    /// <list type="bullet">
    /// <item><c>tags_edited_at</c> freezes the brand tags exactly like an operator edit (F18.3) —
    /// re-scan/backfill never overwrites them.</item>
    /// <item><c>cue_analyzed_at</c> / <c>energy_analyzed_at</c> / <c>bpm_analyzed_at</c> are set
    /// whether or not the analyzer found boundaries/energy/tempo, so the F13/F17/STORY-142 backfill
    /// predicates (<c>*_analyzed_at IS NULL</c>) never re-claim this row — mirrors what
    /// <see cref="WriteEnrichmentAsync"/> does for a scanned file's first enrichment pass. Authored
    /// rows are TTS/jingle content: aubio has no tempo to find, so <c>bpm</c> itself stays null
    /// (attempted-not-applicable, same shape as cue/energy).</item>
    /// <item><c>year_lookup_at</c> is likewise stamped unconditionally (<c>year</c> stays null) —
    /// authored station patter/jingles are never real-world releases, so they must never be sent to
    /// MusicBrainz; without this stamp the F48.3 claim predicate (<c>year IS NULL AND
    /// year_lookup_at IS NULL</c> + non-blank artist/title) would claim every authored row exactly
    /// once, leaking its (station-authored) artist/title to an external service (X5 review finding —
    /// the same invariant <c>bpm_analyzed_at</c> protects, applied to the fourth sentinel).</item>
    /// </list>
    /// An unknown <see cref="AuthoredMediaInsert.LibraryId"/> is rejected by the existing foreign key
    /// on <c>library.media.library_id</c> — the exception is intentionally left unmapped; the single
    /// statement means a rejected insert has already written nothing.
    /// </summary>
    public async Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            insert into library.media (
              path, format, size_bytes, mtime, state, library_id,
              duration_ms, sample_rate, channels, bitrate_kbps,
              title, artist, tags_edited_at,
              integrated_lufs, true_peak_dbtp, measurable,
              cue_in_sec, cue_out_sec, cue_analyzed_at,
              intro_energy, outro_energy, energy_analyzed_at,
              bpm_analyzed_at,
              year_lookup_at,
              enriched_at
            ) values (
              @path, @format, @sizeBytes, @mtime, 'ready', @libraryId,
              @durationMs, @sampleRate, @channels, @bitrateKbps,
              @title, @artist, now(),
              @integratedLufs, @truePeakDbtp, @measurable,
              @cueInSec, @cueOutSec, now(),
              @introEnergy, @outroEnergy, now(),
              now(),
              now(),
              now()
            )
            returning id
            """,
            new
            {
                path = insert.Path,
                format = insert.Format,
                sizeBytes = insert.SizeBytes,
                mtime = insert.Mtime,
                libraryId = insert.LibraryId,
                durationMs = insert.DurationMs,
                sampleRate = insert.SampleRate,
                channels = insert.Channels,
                bitrateKbps = insert.BitrateKbps,
                title = insert.Tags.Title,
                artist = insert.Tags.Artist,
                integratedLufs = insert.Loudness.IntegratedLufs,
                truePeakDbtp = insert.Loudness.TruePeakDbtp,
                measurable = insert.Loudness.Measurable,
                cueInSec = insert.Cue?.CueInSec,
                cueOutSec = insert.Cue?.CueOutSec,
                introEnergy = insert.Energy?.IntroEnergy,
                outroEnergy = insert.Energy?.OutroEnergy,
            },
            cancellationToken: ct));
    }

    // ── Re-enrichment scheduling (IAdminMediaReenrichment — Epic J, STORY-051) ─────────────────────

    /// <summary>
    /// Builds the SET-clause fragment for a re-enrichment sentinel reset from a <see cref="ReenrichFields"/>
    /// bit-field. Every column name is a compile-time constant; no user input is ever interpolated into
    /// the SQL string (no injection surface in the SET clause — fields is parsed to the enum before
    /// reaching this method).
    ///
    /// Per-flag column mapping (SPEC F20.10, F46.4):
    ///   Cue     — cue_in_sec, cue_out_sec, cue_analyzed_at → null; state unchanged.
    ///   Energy  — intro_energy, outro_energy, energy_analyzed_at → null; state unchanged.
    ///   Loudness — integrated_lufs, true_peak_dbtp, measurable → null; state = 'discovered'.
    ///   Tags    — tags_edited_at → null; state = 'discovered'.
    ///   Bpm     — bpm, bpm_analyzed_at → null; state unchanged.
    ///   Year    — year_lookup_at → null ONLY; year itself is untouched; state unchanged (SPEC F48.6).
    /// When both Loudness and Tags are requested, <c>state = 'discovered'</c> appears once.
    /// </summary>
    static string BuildReenrichSetClauses(ReenrichFields fields)
    {
        var parts = new List<string>();

        if (fields.HasFlag(ReenrichFields.Cue))
        {
            parts.Add("cue_in_sec = null");
            parts.Add("cue_out_sec = null");
            parts.Add("cue_analyzed_at = null");
        }

        if (fields.HasFlag(ReenrichFields.Energy))
        {
            parts.Add("intro_energy = null");
            parts.Add("outro_energy = null");
            parts.Add("energy_analyzed_at = null");
        }

        if (fields.HasFlag(ReenrichFields.Loudness))
        {
            parts.Add("integrated_lufs = null");
            parts.Add("true_peak_dbtp = null");
            parts.Add("measurable = null");
        }

        if (fields.HasFlag(ReenrichFields.Tags))
        {
            parts.Add("tags_edited_at = null");
        }

        if (fields.HasFlag(ReenrichFields.Bpm))
        {
            parts.Add("bpm = null");
            parts.Add("bpm_analyzed_at = null");
        }

        if (fields.HasFlag(ReenrichFields.Year))
        {
            // Sentinel only (SPEC F48.6) — deliberately NOT "year = null" alongside it; a wrong
            // year's correction surface is the F18 PATCH, not a re-roll of this lookup.
            parts.Add("year_lookup_at = null");
        }

        // state = 'discovered' is added once regardless of whether Loudness, Tags, or both are set.
        if (ResetsState(fields))
            parts.Add("state = 'discovered'");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// True when the reset drops the row to <c>state = 'discovered'</c> (Loudness and/or Tags).
    /// Those rows must be handed to the enrichment worker via <see cref="EnqueueForEnrichment"/> —
    /// the discovered path is channel-fed (scanner deltas + startup recovery only), so without an
    /// explicit enqueue the row sits out of rotation until the next restart. Cue/energy resets keep
    /// <c>state = 'ready'</c> and are reclaimed by the polling backfill predicates instead.
    /// </summary>
    static bool ResetsState(ReenrichFields fields) =>
        fields.HasFlag(ReenrichFields.Loudness) || fields.HasFlag(ReenrichFields.Tags);

    /// <summary>
    /// Best-effort, non-blocking hand-off to the enrichment worker. A full channel is not an error:
    /// the sentinel reset has already committed, and the startup recovery query
    /// (<see cref="ListPendingEnrichmentAsync"/>) re-drives any <c>discovered</c> row it missed.
    /// </summary>
    void EnqueueForEnrichment(long id)
    {
        if (!enrichQueue.Writer.TryWrite(id))
            logger.LogWarning(
                "Enrichment queue full; media {Id} stays 'discovered' and will be recovered on the next startup", id);
    }

    /// <summary>
    /// Sentinel-resets the columns selected by <paramref name="fields"/> on the row identified by
    /// <paramref name="id"/> in a single UPDATE. Reaches any existing row regardless of
    /// <paramref name="scope"/> (SPEC F43.3 — scope is a curation filter, not an access gate).
    /// The WHERE clause matches by id alone, so 0 rows affected means the id does not exist — the
    /// same IDOR-safe existence-first signal used by <see cref="UpdateCoreAsync"/>.
    /// </summary>
    public async Task<ReenrichResult> ScheduleAsync(
        string id,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct)
    {
        if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowId))
            return ReenrichResult.NotFound;

        var setClause = BuildReenrichSetClauses(fields);
        var sql = $"""
            update library.media
            set {setClause}
            where id = @rowId
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { rowId },
            cancellationToken: ct));

        if (affected == 0)
            return ReenrichResult.NotFound;

        if (ResetsState(fields))
            EnqueueForEnrichment(rowId);
        events.Publish(new MediaMutated("reenrich", rowId, 1));
        return ReenrichResult.Scheduled;
    }

    /// <summary>
    /// Sentinel-resets the columns selected by <paramref name="fields"/> on every row matching
    /// <paramref name="filter"/> within <paramref name="scope"/> in a single UPDATE.
    /// Uses <see cref="BuildAdminWhere"/> so the WHERE predicate set is identical to
    /// <see cref="ListAdminAsync"/>. Scope-bound by <c>library_id = ANY(@libraryIds)</c> — always
    /// first. Empty scope → 0, no SQL issued (default-deny). All filter values are Npgsql parameters.
    /// </summary>
    public async Task<int> ScheduleBulkAsync(
        MediaQuery filter,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct)
    {
        // Default-deny: no scope = no rows to update.
        if (scope.IsEmpty) return 0;

        var setClause = BuildReenrichSetClauses(fields);
        var (where, filterParams) = BuildAdminWhere(filter, scope);

        var sql = $"""
            update library.media
            set {setClause}
            where {where}
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Cue/energy-only resets keep state='ready'; the polling backfills reclaim them — a plain
        // count is enough. State-resetting fields need the affected ids back so each row can be
        // handed to the enrichment worker (see EnqueueForEnrichment).
        if (!ResetsState(fields))
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, filterParams, cancellationToken: ct));
            if (affected > 0)
                events.Publish(new MediaMutated("reenrich-bulk", null, affected));
            return affected;
        }

        var ids = (await conn.QueryAsync<long>(new CommandDefinition(
            sql + "\nreturning id", filterParams, cancellationToken: ct))).AsList();

        foreach (var id in ids)
            EnqueueForEnrichment(id);

        if (ids.Count > 0)
            events.Publish(new MediaMutated("reenrich-bulk", null, ids.Count));
        return ids.Count;
    }
}
