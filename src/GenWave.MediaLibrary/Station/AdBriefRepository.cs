using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="IAdBriefStore"/>'s one implementation (SPEC F159.1, F162.1, F162.2, F171.6; STORY-389,
/// STORY-392, STORY-406; PLAN T398, T403b, T432) over <c>station.ad_brief</c> —
/// connection-per-call, mirrors <see cref="AdSpotRepository"/>'s own <see cref="Lazy{T}"/>
/// data-source discipline one table over. No column here needs a raw-text/enum split the way
/// <see cref="AdSpotRow"/> does, so <see cref="AdBrief"/> (the Core-level record itself) is
/// Dapper's own projection target — no separate internal row type.
/// </summary>
sealed class AdBriefRepository(Lazy<NpgsqlDataSource> dataSource) : IAdBriefStore
{
    /// <summary>Every column <see cref="AdBrief"/> projects — one shared literal so
    /// <see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/<see cref="SetEnabledAsync"/> can
    /// never drift from <see cref="UpsertAsync"/>'s own column list.</summary>
    const string Columns = "id, pack_slug, sponsor_id, premise, tone, structure, enabled, created_at";

    /// <summary>SQLSTATE for a foreign-key violation — the house well-known-constant idiom
    /// (<c>ShowRepository</c>/<c>SpecialsRepository</c>/<c>AdminLibraryRepository</c>, no
    /// <c>Npgsql.PostgresErrorCodes</c> dependency), used by <see cref="UninstallPackAsync"/>'s own
    /// race backstop.</summary>
    const string ForeignKeyViolation = "23503";

    /// <summary>Defensive ceiling on <see cref="ListAllAsync"/>'s otherwise-unpaged read — the SAME
    /// <c>AdSpotRepository.MaxUnpagedRows</c> value, one table over: the Briefs tab is an
    /// operator-curated catalog, never expected to approach this, but the read stays bounded rather
    /// than genuinely unbounded (T403b's own YAGNI call on real paging, not a YAGNI call on a
    /// ceiling).</summary>
    const int MaxUnpagedRows = 1000;

    /// <summary>
    /// The <c>on conflict</c> update clause <see cref="UpsertAsync"/>'s own <c>ON CONFLICT (sponsor_id,
    /// premise_key)</c> upsert uses — deliberately omits <c>enabled</c> (T405 review RULING, corrects
    /// the T398-shipped shape): <c>enabled</c> is set ONLY by the INSERT half's own values list (a
    /// brand-new row), never touched again by an UPDATE — see <see cref="IAdBriefStore.UpsertAsync"/>'s
    /// own remarks for the full PRESERVE-on-conflict contract this enforces. <see cref="UpsertAllAsync"/>
    /// carries the SAME never-touch-<c>enabled</c>-on-update rule but can't share this literal —
    /// premise/tone/structure there are updated by plain <c>@parameter</c>, not Postgres' own
    /// <c>excluded.</c> pseudo-table, since that method's own existing-row match (see its remarks) is
    /// never an <c>ON CONFLICT</c> in the first place.
    /// </summary>
    const string ConflictUpdateSet = "premise = excluded.premise, tone = excluded.tone, structure = excluded.structure";

    /// <summary>
    /// <see cref="IAdBriefStore.UpsertAsync"/> — one round trip IS the check (the
    /// <c>Catalog.ArtworkTokenRepository</c>/<c>AnnouncementRepository.InsertAsync</c> lazy-upsert
    /// precedent): <c>on conflict (sponsor_id, premise_key)</c> infers <c>station.ad_brief</c>'s own
    /// <c>ad_brief_sponsor_id_premise_key</c> constraint (db/46 step 9; PLAN T432 retargets this from
    /// the pre-sponsors <c>(pack_slug, brand)</c> key) — <c>premise_key</c> is the STORED fold of
    /// <c>premise</c> (db/06), so two calls whose premise folds identical for the SAME
    /// <paramref name="sponsorId"/> collapse to one row. That constraint is a PLAIN <c>UNIQUE</c>, not
    /// <c>NULLS NOT DISTINCT</c> (db/06's own remarks on <c>ad_brief_sponsor_id_premise_key</c>): a
    /// <see langword="null"/>/blank <paramref name="premise"/> folds to a <see langword="null"/>
    /// <c>premise_key</c>, and Postgres never treats two <see langword="null"/>s as conflicting, so a
    /// SECOND angle-less call always inserts a NEW row rather than updating — SPEC F171.6's own "a
    /// NULL premise is no angle" carve-out, enforced at the constraint, not by this method.
    /// <c>created_at</c> is never in the <c>SET</c> list, so the update half leaves it untouched —
    /// and, as of the T405 review ruling, neither is <c>enabled</c> (see
    /// <see cref="ConflictUpdateSet"/>'s own remarks): <paramref name="enabled"/> only ever lands on
    /// the INSERT half's own values list, so a second call's <paramref name="enabled"/> argument is
    /// silently irrelevant to an EXISTING row — the interface's own remarks name why.
    /// </summary>
    public async Task<AdBrief> UpsertAsync(
        string? packSlug, long sponsorId, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AdBrief>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
            values (@packSlug, @sponsorId, @premise, @tone, @structure, @enabled)
            on conflict (sponsor_id, premise_key) do update
            set {ConflictUpdateSet}
            returning {Columns}
            """,
            new { packSlug, sponsorId, premise, tone, structure, enabled },
            cancellationToken: ct));
    }

    /// <summary>
    /// <see cref="IAdBriefStore.UpsertAllAsync"/> — ONE connection, ONE <see cref="NpgsqlTransaction"/>
    /// wrapping one <c>ON CONFLICT DO UPDATE</c> round trip per declared brief (replaces this
    /// method's own former read-then-write <c>existing</c> CTE, now that db/46 step 9b
    /// carries a real <c>(pack_slug, sponsor_id) WHERE pack_slug IS NOT NULL</c> partial unique index
    /// for the conflict target to name). Applied per-row rather than delete-then-reinsert — a brief's
    /// own <c>enabled</c> flag is exactly the per-row state a blanket delete-then-reinsert would
    /// destroy, the reason this method upserts each brief individually inside the shared transaction
    /// instead (the <see cref="AvatarPackRepository.UpsertAsync"/>/<see cref="FontPackRepository.UpsertAsync"/>
    /// "single-transaction multi-write install" precedent). A failure on ANY brief (the connection
    /// never reaches <see cref="NpgsqlTransaction.CommitAsync"/>) rolls back every row this call would
    /// otherwise have written — never a partially-installed pack.
    ///
    /// <para>
    /// Matches an EXISTING row on <c>(pack_slug, sponsor_id)</c> — deliberately NOT
    /// <see cref="UpsertAsync"/>'s own <c>ON CONFLICT (sponsor_id, premise_key)</c>: a pack's declared
    /// brief for one brand is ONE slot across reinstalls (T405 review F2 — "content refreshes,
    /// operator state persists" on the SAME row), and <c>premise_key</c> is <paramref name="premise"/>
    /// text folded, so keying the match on it would make an ordinary premise-copy edit look like a
    /// BRAND-NEW brief and duplicate the row instead of refreshing it — <c>ad_brief_pack_slug_sponsor_id_key</c>
    /// is what enforces this identity now, a partial index rather than a plain constraint SPECIFICALLY
    /// because an owner-authored brief (<c>pack_slug is null</c>) legitimately keeps several premises
    /// per sponsor (that's what <c>ad_brief_sponsor_id_premise_key</c> scopes), while a PACK brief caps
    /// at one row per sponsor. Every INSERT half hardcodes <c>enabled = true</c> (a brand-new pack
    /// brief is always born live, SPEC F162.2) — never a per-brief parameter, since
    /// <see cref="AdBriefUpsertInput"/> deliberately carries none (that record's own remarks); the
    /// UPDATE half never touches <c>enabled</c> at all, the SAME PRESERVE-on-conflict rule
    /// <see cref="ConflictUpdateSet"/>'s own remarks document one method over — reused verbatim here,
    /// since both methods' UPDATE half sets the identical three columns.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<AdBrief>> UpsertAllAsync(
        string packSlug, IReadOnlyList<AdBriefUpsertInput> briefs, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var results = new List<AdBrief>(briefs.Count);
        foreach (var brief in briefs)
        {
            var row = await conn.QuerySingleAsync<AdBrief>(new CommandDefinition(
                $"""
                insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
                values (@packSlug, @sponsorId, @premise, @tone, @structure, true)
                on conflict (pack_slug, sponsor_id) where pack_slug is not null do update
                set {ConflictUpdateSet}
                returning {Columns}
                """,
                new { packSlug, sponsorId = brief.SponsorId, brief.Premise, brief.Tone, brief.Structure },
                transaction: tx,
                cancellationToken: ct));
            results.Add(row);
        }

        await tx.CommitAsync(ct);
        return results;
    }

    /// <summary><see cref="IAdBriefStore.SampleEnabledAsync"/> — Postgres' own <c>order by random()</c>,
    /// the SAME "let the database pick" shape <c>LibraryAdSpotSource</c>'s own live-Postgres random
    /// read uses one project over; the brief universe is small (an operator-curated catalog, not a
    /// media library), so a full-table <c>ORDER BY random()</c> costs nothing worth a more elaborate
    /// sampling scheme here.</summary>
    public async Task<AdBrief?> SampleEnabledAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            select {Columns}
            from station.ad_brief
            where enabled
            order by random()
            limit 1
            """,
            cancellationToken: ct));
    }

    /// <summary><see cref="IAdBriefStore.ListAllAsync"/> — every brief, any pack/owner mix, newest
    /// created first (T403b's own YAGNI call: see the interface's own remarks for why this is a full
    /// list, never a paged one). Bounded at <see cref="MaxUnpagedRows"/> as a defensive ceiling, not a
    /// real paging mechanism — the SAME ceiling <c>AdSpotRepository</c>'s own unpaged reads already
    /// apply one table over.</summary>
    public async Task<IReadOnlyList<AdBrief>> ListAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdBrief>(new CommandDefinition(
            $"""
            select {Columns}
            from station.ad_brief
            order by created_at desc, id desc
            limit @limit
            """,
            new { limit = MaxUnpagedRows },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// <see cref="IAdBriefStore.CreateOwnerAsync"/> — <c>pack_slug</c> hardcoded <c>null</c> in the
    /// INSERT itself (never trusting a caller-supplied value the way <see cref="UpsertAsync"/> does —
    /// this member exists exactly BECAUSE a caller must never be able to silently update an existing
    /// owner brief the way <see cref="UpsertAsync"/> would), <c>on conflict ... do nothing</c> against
    /// the SAME <c>ad_brief_sponsor_id_premise_key</c> index <see cref="UpsertAsync"/> targets — a
    /// second call for the SAME <paramref name="sponsorId"/>/folded <paramref name="premise"/> pair
    /// (pack-owned or owner-authored — <c>pack_slug</c> plays no part in this constraint, PLAN T432)
    /// collides and inserts nothing; the SAME <see langword="null"/>-premise carve-out
    /// <see cref="UpsertAsync"/>'s own remarks document applies here too (an angle-less create never
    /// collides with an earlier angle-less one). <c>DO NOTHING</c> + <c>QuerySingleOrDefaultAsync</c>
    /// is the one-round-trip conflict check: a <see langword="null"/> result means the INSERT hit the
    /// conflict branch and inserted nothing, which the caller reads as "the key already holds" —
    /// <see cref="IAdBriefStore.CreateOwnerAsync"/>'s own words for this exact outcome (corrects this
    /// method's former "cap already holds" phrasing, which named the wrong concept: nothing here is
    /// a numeric ceiling, it's a duplicate-angle conflict).
    /// </summary>
    public async Task<AdBrief?> CreateOwnerAsync(
        long sponsorId, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
            values (null, @sponsorId, @premise, @tone, @structure, @enabled)
            on conflict (sponsor_id, premise_key) do nothing
            returning {Columns}
            """,
            new { sponsorId, premise, tone, structure, enabled },
            cancellationToken: ct));
    }

    /// <summary><see cref="IAdBriefStore.SetEnabledAsync"/> — a guarded, single-round-trip
    /// <c>UPDATE ... RETURNING</c> (the <c>AdSpotRepository.RunGuardedTransitionAsync</c> shape one
    /// table over, without the xmin guard — see the interface's own remarks for why a brief toggle
    /// carries no If-Match ceremony). <see langword="null"/> back means the <c>WHERE id = @id</c>
    /// matched nothing — an unknown id, the caller's own 404 signal.</summary>
    public async Task<AdBrief?> SetEnabledAsync(long id, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            update station.ad_brief
            set enabled = @enabled
            where id = @id
            returning {Columns}
            """,
            new { id, enabled },
            cancellationToken: ct));
    }

    /// <summary>
    /// The referencing-owner-work guard for <see cref="UninstallPackAsync"/> (SPEC F172.4, PLAN T437)
    /// — text-pinned by a GenWave.MediaLibrary.Tests spec (the <c>VoicePackRepository.DeleteGuardSql</c>
    /// idiom, one seam over). THAT guard sits INSIDE the single DELETE statement it protects; this
    /// uninstall's own write is a multi-step retire-then-delete-then-delete with no single-statement
    /// shape to embed a guard inside (see <see cref="UninstallPackAsync"/>'s own remarks), so this is
    /// instead the READ half of a check-then-act sequence — the <see cref="SponsorRepository"/>'s own
    /// <c>ReadReferencesAsync</c> precedent, one table over. Two result sets, read in order: every
    /// OWNER-referencing <c>station.ad_spot</c> title (any state — EXCLUDING rows this SAME uninstall
    /// would itself retire, <c>source = 'pack' and pack_slug = @packSlug</c>, which are never
    /// themselves a reason to refuse), then every referencing <c>station.show</c> name — each capped
    /// at ten, oldest first.
    /// </summary>
    internal static readonly string UninstallGuardSql =
        """
        select title
        from station.ad_spot
        where sponsor_id in (select id from station.sponsor where pack_slug = @packSlug)
          and not (source = 'pack'::station.ad_source and pack_slug = @packSlug)
        order by created_at
        limit 10;

        select name as title
        from station.show
        where sponsor_id in (select id from station.sponsor where pack_slug = @packSlug)
        order by created_at
        limit 10;
        """;

    /// <summary>
    /// <see cref="IAdBriefStore.UninstallPackAsync"/> — ONE connection, ONE <see cref="NpgsqlTransaction"/>
    /// covering both the READ (<see cref="UninstallGuardSql"/>) and the WRITE (retire, then two
    /// deletes): a check-then-act sequence, not <see cref="VoicePackRepository"/>'s own "the delete IS
    /// the guard" single statement — this uninstall's write touches THREE tables in sequence (retire
    /// <c>station.ad_spot</c>, delete <c>station.ad_brief</c>, delete <c>station.sponsor</c>), which a
    /// single <c>WHERE NOT EXISTS</c>-guarded DELETE has no shape for. Mirrors
    /// <see cref="SponsorRepository"/>'s own <c>DeleteIfUnreferencedAsync</c> check-then-act idiom one
    /// table over, widened to an explicit multi-statement transaction (that method's single
    /// autocommitted DELETE never needed one) and to the retire step SPEC F172.4 adds.
    ///
    /// <para>
    /// <b>A retired pack spot keeps its own sponsor row alive — a real schema conflict, discovered
    /// against live Postgres, not papered over.</b> <c>station.ad_spot.sponsor_id</c> is <c>NOT NULL
    /// REFERENCES station.sponsor(id) ON DELETE RESTRICT</c> (db/46) — retiring, rather than deleting,
    /// a pack's own <c>source = 'pack'</c> spots (the ONLY shape STORY-416 AC3 asks for) leaves that
    /// row in place, still holding the FK. Deleting <c>station.sponsor</c> unconditionally would
    /// therefore throw SQLSTATE 23503 for every sponsor this call JUST retired a spot for — not a
    /// race, the deterministic common case, since <c>AdSpotWorker</c> creates real
    /// <c>source = 'pack'</c> spots in normal operation. An OWNER-authored <c>station.ad_brief</c> row
    /// on a pack sponsor is the SAME kind of deterministic conflict:
    /// <c>station.ad_brief.sponsor_id</c> carries the identical <c>NOT NULL REFERENCES ... ON DELETE
    /// RESTRICT</c> shape, <c>POST /api/ad-briefs</c> accepts any <c>sponsorId</c> including a pack
    /// sponsor's own (<c>GenWave.Host.Api.AdBriefsController.Create</c>'s own remarks), and the brief DELETE
    /// two lines above this one only ever removes rows for THIS <c>pack_slug</c> — an owner brief's own
    /// <c>pack_slug</c> is <see langword="null"/>, so it survives that delete untouched. The sponsor
    /// <c>DELETE</c> below therefore carries its own <c>NOT EXISTS</c> survivor clause covering BOTH
    /// tables: a sponsor with ANY remaining <c>station.ad_spot</c> reference (one of THIS call's own
    /// now-retired rows, or a still-<c>rendering</c> one the retire step deliberately skips — see the
    /// next paragraph — since an owner-authored spot reference would already have refused above), OR
    /// ANY remaining <c>station.ad_brief</c> reference (an owner-authored brief this call never
    /// touches), is left in place rather than attempted and failed.
    /// <see cref="AdPackUninstallResult.Deleted.Sponsors"/> reports the count that actually
    /// went — STORY-416 AC1's "sponsor count is 0" holds exactly when a pack's sponsors carry no spot
    /// or owner-brief history at all (that AC's own given: no owner work, and no spots either); AC3's
    /// own given (three existing pack spots) never claims the sponsor count, only the retirement —
    /// this call keeps both ACs literally true rather than one at the other's expense.
    /// </para>
    ///
    /// <para>
    /// <b>The retire step skips <c>rendering</c> (PLAN T437 review round 2 finding 2).</b>
    /// <c>Station.AdSpotRepository.RetireAsync</c>'s own remarks state the invariant this uninstall
    /// must honour too, one write shape over: "Rendering is deliberately absent from this list — it
    /// stays undiscardable." A pack spot mid-render at uninstall time finishes and resolves on its own
    /// (<c>MarkReadyAsync</c>/<c>MarkFailedAsync</c>) rather than being yanked to <c>retired</c>
    /// out from under the render worker; its sponsor is simply KEPT by the survivor clause above, the
    /// same as a genuinely-retired spot's sponsor — never attempted-and-failed, never silently
    /// discarded mid-flight.
    /// </para>
    ///
    /// <para>
    /// <b>NotFound is a pre-check, not read off delete counts.</b> Now that a sponsor row can
    /// legitimately survive its own delete attempt (the survivor clause above),
    /// "<paramref name="packSlug"/> was never installed" can no longer be inferred from "both deletes
    /// affected zero rows" — a pack whose every sponsor survives (all spot-referenced) would
    /// affect zero sponsor rows despite genuinely having been installed. This reads
    /// <c>EXISTS</c> against both <c>station.ad_brief</c> and <c>station.sponsor</c> BEFORE opening the
    /// write transaction at all (the <c>DeleteIfUnreferencedAsync</c> pre-check precedent, one method
    /// over) and returns <see cref="AdPackUninstallResult.NotFound"/> immediately when neither ever
    /// held a row for <paramref name="packSlug"/>, without writing anything.
    /// </para>
    ///
    /// <para>
    /// <b>A repeat call answers 200 again, forever — that is the contract,
    /// not a bug.</b> Once a pack sponsor survives one uninstall (<see cref="AdPackUninstallResult.
    /// Deleted.KeptSponsors"/> non-empty), that row's own <c>pack_slug</c> column is never cleared, so
    /// the pre-check's <c>exists(station.sponsor where pack_slug = @packSlug)</c> half stays true on
    /// every SUBSEQUENT call for the SAME <paramref name="packSlug"/> — this method can never answer
    /// <see cref="AdPackUninstallResult.NotFound"/> for a slug that still has a kept sponsor standing.
    /// A second call's own <c>RetiredSpots</c> simply reads 0 (nothing left in <c>source = 'pack'</c>
    /// state to retire — the first call already did), <c>Briefs</c> reads 0 the same way, and
    /// <c>KeptSponsors</c> names the identical survivor set again — see
    /// <c>GenWave.Host.Api.AdPackUninstallResponse</c>'s own remarks for this same contract stated from
    /// the route's side.
    /// </para>
    ///
    /// <para>
    /// <b>A caught 23503 must roll back before re-reading — unlike the <c>DeleteIfUnreferencedAsync</c>
    /// precedent.</b> That method's own 23503 catch block re-queries on the SAME connection with no
    /// explicit rollback, because its one DELETE statement is never wrapped in an explicit transaction
    /// in the first place — an autocommitted statement's own failure never leaves the connection
    /// mid-aborted. THIS method's write is a genuine multi-statement transaction, so the instant any
    /// statement inside it raises a SQLSTATE error, Postgres aborts that transaction server-side; any
    /// further query on <c>tx</c> would itself fail with "current transaction is aborted" until an
    /// explicit rollback runs — so the 23503 handler below rolls back FIRST, then re-reads on the
    /// now-autocommitted connection. With the sponsor DELETE's own survivor clause covering both
    /// <c>station.ad_spot</c> AND <c>station.ad_brief</c>, an owner-authored brief on a pack sponsor
    /// needs no race at all —
    /// it is caught deterministically, every time, by the survivor clause itself (see the paragraph
    /// above), never a 23503. The statements that CAN still raise 23503 here are (a) the SHOW-side FK
    /// (<c>station.show.sponsor_id</c>, a nullable <c>ON DELETE RESTRICT</c> FK, db/46 step 14, which
    /// deliberately carries no survivor clause of its own — see the sponsor DELETE's own inline
    /// remarks below), for a sponsor this DELETE would otherwise remove, when a show lands on it
    /// between <see cref="ReadReferencesAsync"/>'s own guard read and this statement, and (b) a
    /// brand-new owner <c>station.ad_spot</c> or <c>station.ad_brief</c> row landing on a pack
    /// sponsor in that same check-then-act window. Both are refused atomically as
    /// <see cref="AdPackUninstallResult.InUse"/> by the catch block below, which rolls back and
    /// re-reads the guard rather than committing a partial uninstall.
    /// </para>
    /// </summary>
    public async Task<AdPackUninstallResult> UninstallPackAsync(string packSlug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var installed = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(select 1 from station.ad_brief where pack_slug = @packSlug)
                or exists(select 1 from station.sponsor where pack_slug = @packSlug)
            """,
            new { packSlug }, cancellationToken: ct));
        if (!installed)
            return new AdPackUninstallResult.NotFound();

        await using var tx = await conn.BeginTransactionAsync(ct);

        var (spotTitles, showNames) = await ReadReferencesAsync(conn, tx, packSlug, ct);
        if (spotTitles.Count > 0 || showNames.Count > 0)
        {
            await tx.RollbackAsync(ct);
            return new AdPackUninstallResult.InUse(spotTitles, showNames);
        }

        int retiredSpots;
        int briefsDeleted;
        int sponsorsDeleted;
        try
        {
            // "and state <> 'rendering'" (PLAN T437 review round 2 finding 2): AdSpotRepository.RetireAsync's
            // own remarks state the invariant this retire step must honour too — "Rendering is
            // deliberately absent from this list — it stays undiscardable." A mid-render pack spot
            // finishes untouched; its sponsor is simply kept (the survivor clause below), never retired
            // out from under the render, never attempted-and-failed against the FK either.
            retiredSpots = await conn.ExecuteAsync(new CommandDefinition(
                """
                update station.ad_spot
                set state = 'retired'::station.ad_state, fail_reason = null, state_changed_at = now(),
                    retired_at = now()
                where source = 'pack'::station.ad_source and pack_slug = @packSlug
                  and state <> 'retired'::station.ad_state and state <> 'rendering'::station.ad_state
                """,
                new { packSlug }, transaction: tx, cancellationToken: ct));

            briefsDeleted = await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.ad_brief where pack_slug = @packSlug",
                new { packSlug }, transaction: tx, cancellationToken: ct));

            // The survivor clause covers the two references that can legitimately outlive an
            // uninstall: a sponsor still named by ANY station.ad_spot row (one of this call's own
            // just-retired rows, or a still-rendering one the exclusion above left untouched — an
            // owner-authored spot reference already refused above), OR by ANY remaining
            // station.ad_brief row (an owner-authored brief on this pack sponsor — this method's own
            // remarks below). The station.show reference is deliberately NOT a survivor clause here:
            // for a sponsor this DELETE would otherwise remove (no spot/brief reference of its own),
            // a show on that sponsor is already answered InUse by ReadReferencesAsync's own guard
            // read above, and a show linked between that read and this statement hits the show FK
            // (23503) and is refused atomically by the catch below — nothing written. (A sponsor the
            // survivor clause already skips for a spot/brief reference is never targeted by this
            // DELETE at all, so a show on THAT sponsor raises no FK here — it is absorbed into the
            // same kept row, no differently than if the show were never there.) A NOT EXISTS clause
            // for station.show here would instead silently skip the sponsor and COMMIT a partial
            // uninstall in that exact race window, which is not this method's contract.
            sponsorsDeleted = await conn.ExecuteAsync(new CommandDefinition(
                """
                delete from station.sponsor
                where pack_slug = @packSlug
                  and not exists (select 1 from station.ad_spot where ad_spot.sponsor_id = sponsor.id)
                  and not exists (select 1 from station.ad_brief where ad_brief.sponsor_id = sponsor.id)
                """,
                new { packSlug }, transaction: tx, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            // A race: something inserted a new owner reference between this call's own guard read
            // above and its write here. Roll back explicitly before re-reading — see this method's own
            // remarks for why DeleteIfUnreferencedAsync's re-query-without-rollback shape does not
            // apply to an explicit multi-statement transaction.
            await tx.RollbackAsync(ct);
            var (racedSpotTitles, racedShowNames) = await ReadReferencesAsync(conn, null, packSlug, ct);
            return new AdPackUninstallResult.InUse(racedSpotTitles, racedShowNames);
        }

        // KeptSponsors: every pack-owned
        // sponsor row STILL standing for this slug — the survivor clause above is the only reason one
        // can be — read in the SAME transaction as the writes above, before commit, ordered by id
        // (AdPackUninstallResult.Deleted.KeptSponsors' own remarks).
        var keptSponsors = (await conn.QueryAsync<Sponsor>(new CommandDefinition(
            $"""
            select {SponsorRepository.Columns}
            from station.sponsor
            where pack_slug = @packSlug
            order by id
            """,
            new { packSlug }, transaction: tx, cancellationToken: ct))).AsList();

        await tx.CommitAsync(ct);
        return new AdPackUninstallResult.Deleted(briefsDeleted, sponsorsDeleted, retiredSpots, keptSponsors);
    }

    /// <summary>Runs <see cref="UninstallGuardSql"/> and projects its two result sets — shared by
    /// <see cref="UninstallPackAsync"/>'s own pre-write guard read (<paramref name="tx"/> non-null) and
    /// its post-rollback race re-read (<paramref name="tx"/> null, the connection back in
    /// autocommit).</summary>
    static async Task<(IReadOnlyList<string> SpotTitles, IReadOnlyList<string> ShowNames)> ReadReferencesAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string packSlug, CancellationToken ct)
    {
        await using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            UninstallGuardSql, new { packSlug }, transaction: tx, cancellationToken: ct));
        var spotTitles = (await multi.ReadAsync<string>()).AsList();
        var showNames = (await multi.ReadAsync<string>()).AsList();
        return (spotTitles, showNames);
    }
}
