using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// <see cref="IRotFindingStore"/>'s one implementation (SPEC F153.1-F153.3, F153.9; STORY-374,
/// STORY-375; PLAN T372, gh-#529) over <c>library.rot_finding</c> — connection-per-call against the
/// library's own <see cref="NpgsqlDataSource"/>, the same shape <see cref="MediaRotationRepository"/>/
/// <see cref="MediaThumbRepository"/> already establish one seam over.
///
/// <para>
/// <b>Reconcile is set-based, in one transaction, per postgres-dba rule 7</b> (F153.2's "as built at
/// T372" amendment, ORCHESTRATOR ruling): every reconcile method here is exactly two statements —
/// (1) an <c>insert … select … where &lt;predicate&gt; on conflict (media_id, kind) do update</c>
/// that opens a fresh finding or re-opens a <see cref="RotState.Resolved"/> one (a
/// <see cref="RotState.Dismissed"/> row is untouched by the <c>WHERE</c> gate on the conflict
/// action; an already-<see cref="RotState.Open"/> row has its evidence refreshed but keeps its own
/// <c>opened_at</c> — only a genuine resolved→open transition stamps a fresh one), then (2) an
/// <c>update … set state = 'resolved', resolved_at = now() where state = 'open' and not exists
/// (&lt;predicate&gt;)</c> that resolves whatever no longer matches. Both statements share the
/// SAME predicate text and run inside ONE <see cref="NpgsqlTransaction"/> — never a per-row C# loop.
/// </para>
/// </summary>
sealed class RotFindingRepository(NpgsqlDataSource dataSource) : IRotFindingStore
{
    /// <summary>
    /// T372 review MED-2: the dead_file predicate text, shared verbatim by the open/re-open half
    /// and the resolve half's own <c>not exists (...)</c> — the <c>MediaRepository.PlayablePredicate</c>
    /// idiom, one constant interpolated everywhere the SAME logical condition is checked so the two
    /// halves can never drift apart.
    /// </summary>
    const string DeadFilePredicate =
        "(m.state = 'failed' or (m.state = 'unavailable' and m.unavailable_since < now() - @grace))";

    /// <summary>
    /// T373 review MED-1: the <c>on conflict (media_id, kind) do update</c> tail
    /// <see cref="ReconcileDeadFilesAsync"/>'s own insert half and <see cref="OpenDeadFileAsync"/>
    /// share VERBATIM — a second <c>DeadFilePredicate</c>-style constant so "the SAME shape" this
    /// type's own remarks and <see cref="OpenDeadFileAsync"/>'s own doc comment already claimed is
    /// actually enforced by the compiler rather than by two hand-kept-in-sync copies.
    ///
    /// <para>
    /// T374 review HIGH-1: <c>group_key</c> is refreshed on every re-open, not just stamped once —
    /// a row whose <c>artist_key</c>/<c>title_key</c>/<c>title_variant</c> change while it STAYS in
    /// <c>find_near_duplicates</c> (moving to a different group, not dropping out of the function
    /// entirely) hits this same conflict path with a fresh <c>group_key</c> that must overwrite the
    /// stale one. Harmless for <see cref="ReconcileDeadFilesAsync"/>/<see cref="OpenDeadFileAsync"/>:
    /// neither INSERT lists a <c>group_key</c> column, and the column has no <c>DEFAULT</c>, so
    /// <c>excluded.group_key</c> is NULL for both — the same NULL the column already held.
    /// </para>
    /// </summary>
    const string OpenOrReopenOnConflict =
        """
        on conflict (media_id, kind) do update
          set state       = 'open',
              evidence    = excluded.evidence,
              group_key   = excluded.group_key,
              opened_at   = case when library.rot_finding.state = 'resolved'
                                 then excluded.opened_at
                                 else library.rot_finding.opened_at end,
              resolved_at = null,
              updated_at  = now()
          where library.rot_finding.state <> 'dismissed'
        """;

    /// <summary>
    /// <see cref="RotKind.StaleMetadata"/>'s base row scope (SPEC F153.6; STORY-377; PLAN T375;
    /// ORCHESTRATOR ruling): deliberately NOT <see cref="MediaRepository.PlayablePredicate"/> — that
    /// predicate requires <c>m.measurable</c> true, which would exclude every row this pass exists
    /// to flag for a <see langword="false"/> <c>measurable</c> value. Shared verbatim by the
    /// insert-select half and the resolve half's own <c>not exists (...)</c>.
    /// </summary>
    const string StaleMetadataScope =
        "m.state = 'ready' and m.eligible and not coalesce(r.never_play, false)";

    /// <summary>
    /// The five <see cref="RotKind.StaleMetadata"/> fields, in evidence order (SPEC F153.6;
    /// ORCHESTRATOR ruling): each <c>case</c> contributes its own field name only when that field is
    /// stale AND, for the three operator-patchable fields, the row has never been operator-edited
    /// (<c>tags_edited_at is null</c>) — <c>moods</c>/<c>measurable</c> carry no such exemption.
    /// <c>array_remove(…, null)</c> uses <c>IS NOT DISTINCT FROM</c> semantics, so it drops every
    /// non-stale field's own NULL contribution; <c>to_jsonb</c> renders whatever remains as a JSON
    /// array of strings — <c>[]</c>, never <see langword="null"/>, when nothing is stale. Aliased
    /// <c>sf.fields</c> via a LATERAL join at each call site purely for readability (T375 review
    /// LOW-1: Postgres flattens the LATERAL — EXPLAIN shows the expression inlined at both the
    /// filter and the target list, not computed once and reused) — one named expression the scope
    /// filter and the evidence build both read, rather than the same <c>case</c> block written out
    /// twice.
    /// </summary>
    const string StaleFieldsJson =
        """
        to_jsonb(array_remove(array[
            case when m.tags_edited_at is null and nullif(btrim(m.artist), '') is null
                 then 'artist' end,
            case when m.tags_edited_at is null and (
                     nullif(btrim(m.title), '') is null or m.title ~* '^\s*track\s*0*[0-9]+\s*$'
                 ) then 'title' end,
            case when m.tags_edited_at is null and m.year is null and m.year_lookup_missed_at is not null
                 then 'year' end,
            case when m.moods is null and m.mood_tag_missed_at is not null
                 then 'moods' end,
            case when m.measurable = false
                 then 'measurable' end
        ], null))
        """;

    /// <summary>
    /// <see cref="RotKind.Unreachable"/>'s own structural cap on <see cref="ReconcileUnreachableAsync"/>'s
    /// envelope list (SPEC F153.8; STORY-378; PLAN T376) — the Laws' own "the VALUES list row count
    /// is the only thing built at runtime; cap it" rule. T376 review MED-3: this is DATABASE-enforced,
    /// not merely app-side — <c>db/27-segment-schedule-migration.sh</c>'s own
    /// <c>station.segment_schedule</c> DDL CHECKs <c>start_minute % 30 = 0</c> and
    /// <c>end_minute % 30 = 0</c> (the 30-minute step) and carries an
    /// <c>exclude using gist (day_of_week with =, int4range(start_minute, end_minute) with &amp;&amp;)</c>
    /// constraint (no two rows on the same day may overlap at all), so a full week can never carry
    /// more than 48 blocks/day &#215; 7 days = 336 raw rows — and DISTINCT effective tuples (this
    /// method's own input) can never exceed that raw row count. 336 is therefore the true structural
    /// ceiling the SCHEMA itself admits, not an arbitrary guess or a guess resting only on
    /// application code; a caller ever exceeding it can only be a bug upstream (a caller reading a
    /// different/corrupted source entirely), which <see cref="ReconcileUnreachableAsync"/> refuses
    /// outright rather than building an unbounded VALUES list.
    /// </summary>
    internal const int MaxEnvelopeTuples = 336;

    /// <summary>
    /// <see cref="RotKind.Unreachable"/>'s own per-row admission check against the CTE
    /// <c>envelopes(genres, energy_min, energy_max)</c> every <see cref="ReconcileUnreachableAsync"/>
    /// statement builds from the caller's own tuple list (SPEC F153.8; STORY-378; PLAN T376): genre
    /// passes when a tuple carries no genre constraint at all (<c>cardinality(e.genres) = 0</c>) or
    /// the row's own lower-cased genre is IN that tuple's own (already lower-cased by the caller,
    /// T376 ORCHESTRATOR ruling) list — the SAME <c>lower(m.genre) = any(...)</c> idiom
    /// <c>MediaRepository.GetEnvelopeCandidateAsync</c>'s own genre predicate uses; energy passes
    /// when the row's own energy is NULL (admitted by every envelope — the SAME NULL-passes
    /// exemption <see cref="MediaRepository.PlayablePredicate"/>'s own energy-band siblings apply,
    /// SPEC F81.4) or falls inside <c>[energy_min, energy_max]</c>. <c>genre_admitted</c>/<c>admitted</c>
    /// are both <c>bool_or</c> aggregates over every tuple (T376 ORCHESTRATOR ruling): the row is
    /// unreachable when <c>admitted</c> is false; the evidence reason is <c>genre</c> when
    /// <c>genre_admitted</c> is ALSO false, else <c>energy</c> (genre passed somewhere, energy never
    /// did for a tuple where it did). The nested <c>per_envelope</c> subquery computes each half ONCE
    /// per tuple rather than repeating the genre expression inside both aggregates — Postgres
    /// flattens this the same way <see cref="StaleFieldsJson"/>'s own LATERAL remarks describe (T375
    /// review LOW-1), so this is purely a readability choice, not a materialization.
    ///
    /// <para>
    /// <b>T376 review BLOCK-1/2: <c>coalesce(..., false)</c> around the genre <c>= any(...)</c>
    /// comparison</b> — Postgres three-valued logic, not a redundant guard: <c>lower(m.genre) = any(e.genres)</c>
    /// evaluates to SQL NULL (never <c>false</c>) when <c>m.genre</c> is NULL, so under a
    /// genre-constrained tuple, an untagged row's own <c>genre_ok</c> would be NULL, its
    /// <c>bool_or</c> would fold to NULL for that tuple, and <c>UnreachablePredicate</c>'s <c>not
    /// adm.admitted</c> would itself evaluate to NULL — which Postgres treats as NOT TRUE in a
    /// WHERE clause. That silently broke BOTH halves: the insert-select's WHERE never matched the
    /// row (an untagged row against a genre-constrained tuple never gets flagged, even though the
    /// live pool at <c>MediaRepository.cs:464</c> excludes it from that exact envelope — it IS
    /// unreachable), and the resolve half's <c>not exists (... and not adm.admitted)</c> flipped to
    /// TRUE the moment a row's genre went NULL, resolving an already-open finding that never
    /// actually became reachable. <c>coalesce(..., false)</c> collapses the NULL to a definite
    /// <see langword="false"/> so an untagged row against a genre-constrained tuple is unambiguously
    /// NOT admitted by that tuple, exactly matching <c>SegmentEnvelope.Genres</c>'s own documented
    /// contract ("An untagged (NULL genre) track does not satisfy a non-empty list").
    /// </para>
    /// </summary>
    const string EnvelopeAdmissionLateral =
        """
        cross join lateral (
            select
                bool_or(per_envelope.genre_ok) as genre_admitted,
                bool_or(per_envelope.genre_ok and per_envelope.energy_ok) as admitted
            from (
                select
                    (cardinality(e.genres) = 0 or coalesce(lower(m.genre) = any(e.genres), false)) as genre_ok,
                    (m.energy is null or (m.energy >= e.energy_min and m.energy <= e.energy_max)) as energy_ok
                from envelopes e
            ) per_envelope
        ) adm
        """;

    /// <summary>
    /// <see cref="RotKind.Unreachable"/>'s own "no tuple admits this row" predicate — a row is
    /// unreachable when <see cref="EnvelopeAdmissionLateral"/>'s own <c>adm.admitted</c> aggregate is
    /// false. Shared verbatim by the insert-select half and the resolve half's own <c>not exists
    /// (...)</c>, exactly like every sibling predicate in this file.
    /// </summary>
    const string UnreachablePredicate = "not adm.admitted";

    /// <summary>
    /// The <c>with envelopes(...) as (values ...)</c> row list for <paramref name="tupleCount"/>
    /// tuples — <c>(@g0::text[], @emin0, @emax0), (@g1::text[], @emin1, @emax1), ...</c>. Only the
    /// ROW COUNT is built at runtime (the Laws' own rule); every value inside a row stays a bound
    /// parameter, added by <see cref="ReconcileUnreachableAsync"/> under the SAME
    /// <c>g{i}</c>/<c>emin{i}</c>/<c>emax{i}</c> names this method emits.
    /// </summary>
    static string BuildEnvelopesValuesList(int tupleCount) => string.Join(
        ",\n            ", Enumerable.Range(0, tupleCount).Select(i => $"(@g{i}::text[], @emin{i}, @emax{i})"));

    /// <summary>
    /// The <c>with envelopes(genres, energy_min, energy_max) as (values ...)</c> CTE header both
    /// <see cref="BuildUnreachableInsertSql"/> and <see cref="BuildUnreachableResolveSql"/> open
    /// with (T376 review LOW-2) — extracted so the two statements can never drift on the CTE's own
    /// column list or indentation, the same "one definition, every caller shares it" idiom this
    /// file's own predicate constants already follow.
    /// </summary>
    static string EnvelopesCte(int tupleCount) => $"""
        with envelopes(genres, energy_min, energy_max) as (
            values
                {BuildEnvelopesValuesList(tupleCount)}
        )
        """;

    /// <summary>
    /// <see cref="ReconcileUnreachableAsync"/>'s own insert-select half, built for
    /// <paramref name="tupleCount"/> envelope rows. <c>internal static</c>, not <c>private</c>
    /// (STORY-378 AC6): Story378's own AC6 fact calls this directly and asserts the text never
    /// contains <c>"station."</c> — the join stays entirely on the library side by construction,
    /// since envelopes arrive as a bound VALUES list, never a query against
    /// <c>station.segment_schedule</c>.
    /// </summary>
    internal static string BuildUnreachableInsertSql(int tupleCount) => $"""
        {EnvelopesCte(tupleCount)}
        insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
        select
            m.id,
            'unreachable'::library.rot_kind,
            'open'::library.rot_state,
            jsonb_build_object(
                'reason', case when not adm.genre_admitted then 'genre' else 'energy' end,
                'envelopes', (select count(*)::int from envelopes)
            ),
            now(),
            now()
        from library.media m
        left join library.media_rating r on r.media_id = m.id
        {EnvelopeAdmissionLateral}
        where {MediaRepository.PlayablePredicate}
          and {UnreachablePredicate}
        {OpenOrReopenOnConflict}
        """;

    /// <summary>
    /// <see cref="ReconcileUnreachableAsync"/>'s own resolve half — see
    /// <see cref="BuildUnreachableInsertSql"/>'s own remarks for why this is <c>internal static</c>.
    /// </summary>
    internal static string BuildUnreachableResolveSql(int tupleCount) => $"""
        {EnvelopesCte(tupleCount)}
        update library.rot_finding f
        set state = 'resolved', resolved_at = now(), updated_at = now()
        where f.kind = 'unreachable'::library.rot_kind
          and f.state = 'open'
          and not exists (
              select 1
              from library.media m
              left join library.media_rating r on r.media_id = m.id
              {EnvelopeAdmissionLateral}
              where m.id = f.media_id
                and {MediaRepository.PlayablePredicate}
                and {UnreachablePredicate}
          )
        """;

    /// <summary>
    /// <see cref="RotKind.ShelfDust"/>'s own conditions, layered on top of
    /// <see cref="MediaRepository.PlayablePredicate"/> at each call site (SPEC F153.7; STORY-377;
    /// PLAN T375; ORCHESTRATOR ruling): no rotation ledger row, or one with zero plays; discovered
    /// further back than <c>@shelfAge</c>; and no currently-<see cref="RotState.Open"/>
    /// <see cref="RotKind.Unreachable"/> finding of its own (T376's own kind — this pass only ever
    /// READS <c>rot_finding</c> for it, never opens/resolves an <c>unreachable</c> row). Shared
    /// verbatim by the insert-select half and the resolve half's own <c>not exists (...)</c>.
    /// </summary>
    const string ShelfDustPredicate =
        """
        (rot.media_id is null or rot.play_count = 0)
          and m.discovered_at < now() - @shelfAge
          and not exists (
              select 1 from library.rot_finding u
              where u.media_id = m.id
                and u.kind = 'unreachable'::library.rot_kind
                and u.state = 'open'::library.rot_state
          )
        """;

    /// <summary>
    /// <see cref="RotKind.DeadFile"/> (SPEC F153.3, T372's own state-based half — the push-guard
    /// report reason, <c>push_missing</c>, is T373): a row is dead when <c>library.media.state =
    /// 'failed'</c>, or when it is <c>'unavailable'</c> and has stayed that way past <paramref
    /// name="unavailableGrace"/>. Evidence names which half fired and since when — <c>failed</c>
    /// rows have no <c>unavailable_since</c> stamp, so <c>coalesce(unavailable_since, enriched_at,
    /// discovered_at)</c> falls back to the best "since" this codebase actually tracks for them
    /// (ORCHESTRATOR ruling).
    /// </summary>
    public async Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("grace", unavailableGrace);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
            select
                m.id,
                'dead_file'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object(
                    'reason', case when m.state = 'failed' then 'failed' else 'unavailable' end,
                    'since', coalesce(m.unavailable_since, m.enriched_at, m.discovered_at)
                ),
                now(),
                now()
            from library.media m
            where {DeadFilePredicate}
            {OpenOrReopenOnConflict}
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            update library.rot_finding f
            set state = 'resolved', resolved_at = now(), updated_at = now()
            where f.kind = 'dead_file'::library.rot_kind
              and f.state = 'open'
              and not (coalesce(f.evidence->>'reason', '') = 'push_missing' and f.opened_at > now() - @grace)
              and not exists (
                  select 1
                  from library.media m
                  where m.id = f.media_id
                    and {DeadFilePredicate}
              )
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// <see cref="IRotFindingStore.ReconcileNearDuplicatesAsync"/> (SPEC F153.5; STORY-376; PLAN
    /// T374): the same set-based, one-transaction, two-statement shape
    /// <see cref="ReconcileDeadFilesAsync"/> established, over
    /// <c>library.find_near_duplicates(@tolerance)</c> instead of a state predicate on
    /// <c>library.media</c> directly.
    ///
    /// <para>
    /// <b>The function runs exactly once PER STATEMENT</b> — <c>with dups as materialized (...)</c>
    /// forces Postgres to evaluate the (STABLE, not cheap) set-returning function once and reuse the
    /// tuplestore for every self-correlation inside that one statement (the siblings/versions
    /// LATERALs below, and the resolve half's own <c>not exists</c>), rather than re-invoking it per
    /// candidate row. Two statements therefore cost two calls per reconcile — a shared temp table
    /// across both would only save that second call, and <c>Gardener:IntervalMinutes</c> (default
    /// 60) makes the saving immaterial.
    /// </para>
    ///
    /// <para>
    /// <b>group_key IS refreshed on every re-open</b> (T374 review HIGH-1) —
    /// <see cref="OpenOrReopenOnConflict"/>'s own <c>group_key = excluded.group_key</c> SET column
    /// overwrites it every time this INSERT's conflict path fires, so a row whose variant moves it
    /// into a different group while it never drops out of <c>find_near_duplicates</c> gets the new
    /// group_key, not a stale one.
    /// </para>
    /// </summary>
    public async Task ReconcileNearDuplicatesAsync(int toleranceMs, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("tolerance", toleranceMs);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            with dups as materialized (
                select media_id, group_key, title_variant
                from library.find_near_duplicates(@tolerance)
            )
            insert into library.rot_finding (media_id, kind, state, evidence, group_key, opened_at, updated_at)
            select
                d.media_id,
                'near_duplicate'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object(
                    'group_key', d.group_key,
                    'title_variant', d.title_variant,
                    'siblings', coalesce(sib.siblings, '[]'::jsonb),
                    'versions', coalesce(ver.versions, '[]'::jsonb)
                ),
                d.group_key,
                now(),
                now()
            from dups d
            join library.media om on om.id = d.media_id
            left join lateral (
                select jsonb_agg(
                           jsonb_build_object('media_id', s.media_id, 'duration_ms', sm.duration_ms)
                           order by sm.duration_ms
                       ) as siblings
                from dups s
                join library.media sm on sm.id = s.media_id
                where s.group_key = d.group_key and s.media_id <> d.media_id
            ) sib on true
            left join lateral (
                select jsonb_agg(
                           jsonb_build_object(
                               'media_id', v.media_id, 'title', v.title,
                               'title_variant', v.title_variant, 'duration_ms', v.duration_ms
                           ) order by abs(v.duration_ms - om.duration_ms), v.media_id
                       ) as versions
                from (
                    select m.id as media_id, m.title, m.title_variant, m.duration_ms
                    from library.media m
                    left join library.media_rating r on r.media_id = m.id
                    where m.artist_key = om.artist_key
                      and m.title_key = om.title_key
                      and m.id <> d.media_id
                      and not exists (select 1 from dups g where g.media_id = m.id and g.group_key = d.group_key)
                      and {MediaRepository.PlayablePredicate}
                    order by abs(m.duration_ms - om.duration_ms), m.id
                    limit 10
                ) v
            ) ver on true
            {OpenOrReopenOnConflict}
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            with dups as materialized (
                select media_id
                from library.find_near_duplicates(@tolerance)
            )
            update library.rot_finding f
            set state = 'resolved', resolved_at = now(), updated_at = now()
            where f.kind = 'near_duplicate'::library.rot_kind
              and f.state = 'open'
              and not exists (select 1 from dups d where d.media_id = f.media_id)
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// <see cref="IRotFindingStore.ReconcileStaleMetadataAsync"/> (SPEC F153.6; STORY-377; PLAN
    /// T375): the same set-based, one-transaction, two-statement shape
    /// <see cref="ReconcileDeadFilesAsync"/> established, over <see cref="StaleMetadataScope"/> +
    /// <see cref="StaleFieldsJson"/> instead of a single boolean predicate — a row's own
    /// <c>sf.fields</c> LATERAL result decides both whether it is in scope for a finding
    /// (<c>jsonb_array_length(sf.fields) > 0</c>) and what the finding's evidence names.
    /// </summary>
    public async Task ReconcileStaleMetadataAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
            select
                m.id,
                'stale_metadata'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object('fields', sf.fields),
                now(),
                now()
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            cross join lateral (select {StaleFieldsJson} as fields) sf
            where {StaleMetadataScope}
              and jsonb_array_length(sf.fields) > 0
            {OpenOrReopenOnConflict}
            """,
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            update library.rot_finding f
            set state = 'resolved', resolved_at = now(), updated_at = now()
            where f.kind = 'stale_metadata'::library.rot_kind
              and f.state = 'open'
              and not exists (
                  select 1
                  from library.media m
                  left join library.media_rating r on r.media_id = m.id
                  cross join lateral (select {StaleFieldsJson} as fields) sf
                  where m.id = f.media_id
                    and {StaleMetadataScope}
                    and jsonb_array_length(sf.fields) > 0
              )
            """,
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// <see cref="IRotFindingStore.ReconcileUnreachableAsync"/> (SPEC F153.8; STORY-378; PLAN T376):
    /// the same set-based, one-transaction, two-statement shape <see cref="ReconcileDeadFilesAsync"/>
    /// established, over a caller-supplied VALUES list instead of a predicate against
    /// <c>library.media</c>'s own columns or a plpgsql function — see
    /// <see cref="BuildUnreachableInsertSql"/>/<see cref="BuildUnreachableResolveSql"/> for the
    /// statement text itself.
    /// </summary>
    public async Task ReconcileUnreachableAsync(IReadOnlyList<EnvelopeTuple> envelopes, CancellationToken ct)
    {
        if (envelopes.Count == 0)
            throw new ArgumentException(
                "At least one envelope tuple is required — the caller's own station-default fallback " +
                "guarantees one even for an empty schedule grid.",
                nameof(envelopes));
        if (envelopes.Count > MaxEnvelopeTuples)
            throw new ArgumentException(
                $"At most {MaxEnvelopeTuples} envelope tuples are supported per reconcile.", nameof(envelopes));

        var parameters = new DynamicParameters();
        for (var i = 0; i < envelopes.Count; i++)
        {
            parameters.Add($"g{i}", envelopes[i].GenresLower.ToArray());
            parameters.Add($"emin{i}", envelopes[i].EnergyMin);
            parameters.Add($"emax{i}", envelopes[i].EnergyMax);
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            BuildUnreachableInsertSql(envelopes.Count), parameters, transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            BuildUnreachableResolveSql(envelopes.Count), parameters, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// <see cref="IRotFindingStore.ReconcileShelfDustAsync"/> (SPEC F153.7; STORY-377; PLAN T375):
    /// the same set-based, one-transaction, two-statement shape <see cref="ReconcileDeadFilesAsync"/>
    /// established, over <see cref="MediaRepository.PlayablePredicate"/> + <see cref="ShelfDustPredicate"/>.
    /// </summary>
    public async Task ReconcileShelfDustAsync(TimeSpan shelfAge, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("shelfAge", shelfAge);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
            select
                m.id,
                'shelf_dust'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object(
                    'discovered_at', m.discovered_at,
                    'days_on_shelf', floor(extract(epoch from now() - m.discovered_at) / 86400)::int
                ),
                now(),
                now()
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            left join library.media_rotation rot on rot.media_id = m.id
            where {MediaRepository.PlayablePredicate}
              and {ShelfDustPredicate}
            {OpenOrReopenOnConflict}
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            update library.rot_finding f
            set state = 'resolved', resolved_at = now(), updated_at = now()
            where f.kind = 'shelf_dust'::library.rot_kind
              and f.state = 'open'
              and not exists (
                  select 1
                  from library.media m
                  left join library.media_rating r on r.media_id = m.id
                  left join library.media_rotation rot on rot.media_id = m.id
                  where m.id = f.media_id
                    and {MediaRepository.PlayablePredicate}
                    and {ShelfDustPredicate}
              )
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// <see cref="IRotFindingStore.OpenDeadFileAsync"/> (SPEC F153.4; STORY-375; PLAN T373): the
    /// push guard's own single-row report — the SAME open/re-open shape
    /// <see cref="ReconcileDeadFilesAsync"/>'s own insert half uses (literally: both interpolate
    /// <see cref="OpenOrReopenOnConflict"/>), narrowed to <paramref name="mediaId"/> via
    /// <c>where m.id = @mediaId</c> instead of the reconcile's set-based predicate. No resolve half
    /// here: this write only ever OPENS or RE-OPENS — resolving a push_missing finding is
    /// <see cref="ReconcileDeadFilesAsync"/>'s own job, subject to the flap guard immediately above
    /// (ORCHESTRATOR ruling, STORY-375 AC3-AC5).
    ///
    /// <para>
    /// T373 review LOW-4: a report against an ALREADY-<see cref="RotState.Open"/> finding (any
    /// reason — <c>failed</c>, <c>unavailable</c>, or a prior <c>push_missing</c>) overwrites its
    /// <c>evidence</c> unconditionally, exactly like <see cref="ReconcileDeadFilesAsync"/>'s own
    /// insert half does for a state-based re-fire — so a push-guard report against a row the state
    /// half already opened makes <c>evidence.reason</c> read <c>push_missing</c> until the NEXT
    /// <see cref="ReconcileDeadFilesAsync"/> tick restores the state-based reason. <c>opened_at</c>
    /// is NOT bumped for an already-open row (the <c>case when ... = 'resolved'</c> guard inside
    /// <see cref="OpenOrReopenOnConflict"/>), so this overwrite can never itself re-arm the flap
    /// guard's own grace window.
    /// </para>
    /// </summary>
    public async Task OpenDeadFileAsync(long mediaId, string reason, CancellationToken ct)
    {
        var parameters = new { mediaId, reason };

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
            select
                m.id,
                'dead_file'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object('reason', @reason, 'since', now()),
                now(),
                now()
            from library.media m
            where m.id = @mediaId
            {OpenOrReopenOnConflict}
            """,
            parameters,
            cancellationToken: ct));
    }

    /// <summary>
    /// STORY-374 AC4: an <c>open</c> row moves to <c>dismissed</c>; anything else (unknown id,
    /// already <c>dismissed</c>, or currently <c>resolved</c>) matches zero rows and returns
    /// <see langword="false"/> — a plain conditional <c>UPDATE</c>, no read-then-write TOCTOU gap.
    /// </summary>
    public async Task<bool> DismissAsync(long findingId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            update library.rot_finding
            set state = 'dismissed', dismissed_at = now(), updated_at = now()
            where id = @findingId and state = 'open'
            """,
            new { findingId },
            cancellationToken: ct));

        return rows > 0;
    }

    /// <summary>
    /// Bounded, paged (T372 review LOW-2; T377 pages it further for the admin surface, SPEC F153.9)
    /// — conditional predicates appended only when the matching filter is supplied, the same
    /// <c>MediaRotationRepository.AppendSafeExclusion</c>-style short-circuit rather than a
    /// nullable-cast comparison in SQL. <c>evidence::text</c> keeps the jsonb column opaque to
    /// Dapper (the <c>FontPack.Definition</c> precedent) — this Core-level record never parses it.
    /// <c>order by opened_at desc</c> is covered by the <c>(kind, state, opened_at desc)</c> index
    /// (db/41) rather than a sequential scan over the forever-growing table.
    /// </summary>
    public async Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (kind is not null)
        {
            conditions.Add("kind = @kind::library.rot_kind");
            parameters.Add("kind", ToKindText(kind.Value));
        }

        if (state is not null)
        {
            conditions.Add("state = @state::library.rot_state");
            parameters.Add("state", ToStateText(state.Value));
        }

        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        var where = conditions.Count > 0 ? "where " + string.Join(" and ", conditions) : "";

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RotFindingRow>(new CommandDefinition(
            $"""
            select id, media_id, kind::text as kind, state::text as state, group_key,
                   evidence::text as evidence, opened_at, resolved_at, dismissed_at, updated_at
            from library.rot_finding
            {where}
            order by opened_at desc
            limit @limit offset @offset
            """,
            parameters,
            cancellationToken: ct));

        return rows.Select(ToFinding).ToList();
    }

    /// <summary>
    /// SPEC F153.9's <c>GET /api/status</c> Gardener tile (T377): one grouped count over
    /// <c>state = 'open'</c>. A kind with no open findings is simply absent from the returned
    /// dictionary rather than present with a zero.
    /// </summary>
    public async Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RotFindingKindCountRow>(new CommandDefinition(
            """
            select kind::text as kind, count(*)::int as count
            from library.rot_finding
            where state = 'open'
            group by kind
            """,
            cancellationToken: ct));

        return rows.ToDictionary(r => ParseKind(r.Kind), r => r.Count);
    }

    static RotFinding ToFinding(RotFindingRow row) => new(
        row.Id, row.MediaId, ParseKind(row.Kind), ParseState(row.State), row.GroupKey, row.Evidence,
        row.OpenedAt, row.ResolvedAt, row.DismissedAt, row.UpdatedAt);

    static string ToKindText(RotKind kind) => kind switch
    {
        RotKind.DeadFile => "dead_file",
        RotKind.NearDuplicate => "near_duplicate",
        RotKind.StaleMetadata => "stale_metadata",
        RotKind.ShelfDust => "shelf_dust",
        RotKind.Unreachable => "unreachable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped RotKind."),
    };

    static RotKind ParseKind(string kind) => kind switch
    {
        "dead_file" => RotKind.DeadFile,
        "near_duplicate" => RotKind.NearDuplicate,
        "stale_metadata" => RotKind.StaleMetadata,
        "shelf_dust" => RotKind.ShelfDust,
        "unreachable" => RotKind.Unreachable,
        _ => throw new InvalidOperationException($"Unrecognised library.rot_kind value '{kind}'."),
    };

    static string ToStateText(RotState state) => state switch
    {
        RotState.Open => "open",
        RotState.Dismissed => "dismissed",
        RotState.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped RotState."),
    };

    static RotState ParseState(string state) => state switch
    {
        "open" => RotState.Open,
        "dismissed" => RotState.Dismissed,
        "resolved" => RotState.Resolved,
        _ => throw new InvalidOperationException($"Unrecognised library.rot_state value '{state}'."),
    };
}
