using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process store over <c>station.announcement</c> (SPEC F143, STORY-357, PLAN T337) — the
/// durable half of the House Voice epic: an accepted announcement either airs or shows exactly why it
/// didn't, and no lifecycle transition below ever deletes a row (SPEC F143.2).
///
/// Connection-per-call against a lazily-built station_svc <see cref="NpgsqlDataSource"/> — the same
/// "resolving must never be enough to trigger a connection attempt" discipline every other
/// station-schema store in this directory documents for its own <see cref="Lazy{T}"/> constructor
/// parameter (see <see cref="RequestRepository"/>'s own remarks).
///
/// <para>
/// <b>Implements <see cref="IAnnouncementStore"/> directly (PLAN T339).</b> T337 shipped this class
/// with no <c>GenWave.Core.Abstractions</c> seam at all — <c>IAnnouncementSource</c> (T338) is a
/// narrower, vend-only Core seam a Host-side adapter implements OVER this repository (PLAN T341), not
/// this repository itself, so it didn't apply here. <c>IAnnouncementStore</c> is different: the
/// endpoint's own needs (insert-or-collapse, pending count) map onto this class's EXISTING members
/// closely enough that a Host-side adapter would add nothing but indirection — the same
/// <see cref="ILiquidsoapControl"/>/<see cref="ShowRepository"/> "the repository implements the port
/// directly" shape <c>AnnouncementServiceCollectionExtensions</c>'s own remarks now follow too.
/// </para>
///
/// <para>
/// <b>ALSO implements <see cref="IAnnouncementSource"/> directly (PLAN T341).</b> The narrower
/// vend-only claim seam this class's own remarks above deferred to "a Host-side adapter" turns out
/// to need nothing this class doesn't already have — <see cref="ClaimOldestAsync"/>'s own SQL IS the
/// claim, so <see cref="ClaimDeliverableAsync"/> below is a thin, mapping-only wrapper over it, the
/// SAME shape <see cref="InsertOrCollapseAsync"/> already is over <see cref="InsertAsync"/>. The
/// SPEC F145.2 SpectatorMode refusal is deliberately NOT here: it lives behind a Host-side decorator
/// (the <c>MediaExistencePushGuard</c>/gh-#612 wrap-in-DI shape) registered OVER this class's own
/// <see cref="IAnnouncementSource"/> registration, so this repository — and every caller of the
/// narrower seam, including <c>GenWave.Orchestration.Orchestrator</c> — never reads Host privacy
/// state at all. See <see cref="AnnouncementServiceCollectionExtensions.AddAnnouncementStore"/> for
/// the registration/decoration split.
/// </para>
///
/// <para>
/// <b>ALSO implements <see cref="IAnnouncementLifecycle"/> directly (PLAN T343).</b> The three
/// lifecycle guardians' own seam — every member on it (<see cref="MarkAiredAsync"/>,
/// <see cref="FindClaimedPastGraceAsync"/>, <see cref="ReArmAsync"/>, <see cref="ExpireStaleAsync"/>,
/// <see cref="DeclineAllLiveAsync"/>) was ALREADY on this class since T337; T343's own job was
/// narrower still — give Host a seam to depend on rather than the concrete repository type, the SAME
/// "repository implements the port directly" shape this class's own remarks already establish twice
/// above.
/// </para>
/// </summary>
sealed class AnnouncementRepository(Lazy<NpgsqlDataSource> dataSource)
    : IAnnouncementStore, IAnnouncementSource, IAnnouncementLifecycle
{
    /// <summary>The endpoint-facing default TTL (SPEC F143.1) — 15 minutes. A caller that knows a
    /// bounded override (60-3600s, the endpoint's own job to enforce) passes it explicitly; omitting
    /// <c>ttl</c> on <see cref="InsertAsync"/> gets this value.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(900);

    // Postgres SQLSTATE for check_violation — mirrors ShowRepository's own UniqueViolation/
    // ForeignKeyViolation constants one column over. station.announcement's only CHECK guarding
    // untrusted input is the 280-char message cap (db/40); the state/source columns are always
    // written from a closed C# enum mapping, never straight from a caller, so a 23514 reaching
    // InsertOrCollapseAsync can only be that cap firing (see that method's own remarks).
    const string CheckViolation = "23514";

    const string SelectColumns =
        """
        select id, message, verbatim, requested_voice, source, state, decline_reason,
               collapse_count, created_at, expires_at, claimed_at, aired_at, state_changed_at
        from station.announcement
        """;

    /// <summary>
    /// Accepts a new announcement (SPEC F143.1), or folds it into an already-pending, still-deliverable
    /// row whose text is case-folded identical (SPEC F143.5, STORY-357 AC4) — one round trip either
    /// way. The collapse target is deliberately the SAME set <see cref="ClaimOldestAsync"/> vends from
    /// (<c>state = 'pending' and expires_at &gt; now()</c>), not <c>state = 'pending'</c> alone: a
    /// pending row whose TTL has already passed but the lifecycle sweep
    /// (<see cref="ExpireStaleAsync"/>) has not yet reached is undeliverable, so a fresh submission must
    /// never fold into it and inherit its stale, already-passed expiry (collapse never re-computes
    /// <c>expires_at</c> on the row it folds into — see the collapse-count fact this claim mirrors) —
    /// it lands its own new row instead. <c>state</c> is left to the column's own <c>'pending'</c>
    /// default (never named in the INSERT list, the same <see cref="RequestRepository.InsertAsync"/>
    /// idiom); <c>expires_at</c> is computed IN SQL as <c>now() + ttl</c> so the stamp reflects the
    /// database's own clock, not this process's.
    ///
    /// <para>
    /// <b>Race posture.</b> The single statement below is a <c>WITH</c> chain: an <c>UPDATE</c>
    /// against any deliverable row whose case-folded message matches, followed by an
    /// <c>INSERT ... WHERE NOT EXISTS</c> that only fires if the <c>UPDATE</c> touched nothing — the
    /// same "one round trip IS the check" shape <see cref="Catalog.ArtworkTokenRepository"/> documents
    /// for its own lazy upsert. Unlike that repository's <c>UPDATE</c> against an ALREADY-EXISTING row
    /// (locked by primary key, so a second concurrent caller blocks and re-reads the winner), the very
    /// FIRST submission of a brand-new message text has no existing row to lock against: two truly
    /// simultaneous inserts of the exact same never-before-seen text could each find the collapse
    /// <c>UPDATE</c> touching zero rows and both fall through to <c>INSERT</c>, landing two rows
    /// instead of one. This is accepted, not closed, for this feature's traffic shape: the caller
    /// (T339's endpoint) enforces the accepted-rate cap (SPEC F143.4, 6/min) BEFORE this method ever
    /// runs, and SPEC F143.5's own aim ("one airing says it once") is best-effort de-duplication of
    /// repeats, not a uniqueness guarantee this store must enforce at all costs.
    /// </para>
    /// </summary>
    public async Task<long> InsertAsync(
        string message, bool verbatim, string? requestedVoice, AnnouncementSource source, TimeSpan? ttl, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            with dup as (
                update station.announcement
                set collapse_count = collapse_count + 1
                where id = (
                    select id from station.announcement
                    where state = 'pending' and expires_at > now() and lower(message) = lower(@Message)
                    order by created_at asc, id asc
                    limit 1
                )
                returning id
            ),
            ins as (
                insert into station.announcement (message, verbatim, requested_voice, source, expires_at)
                select @Message, @Verbatim, @RequestedVoice, @Source, now() + @Ttl
                where not exists (select 1 from dup)
                returning id
            )
            select id from dup
            union all
            select id from ins
            """,
            new
            {
                Message = message,
                Verbatim = verbatim,
                RequestedVoice = requestedVoice,
                Source = ToSourceText(source),
                Ttl = ttl ?? DefaultTtl,
            },
            cancellationToken: ct));
    }

    /// <summary>Maps <see cref="AnnouncementSource"/> to <c>station.announcement.source</c>'s own
    /// lowercase text values — mirrors <c>PersonaAvatarRepository.ToSourceText</c>'s own exhaustive,
    /// throwing switch exactly, one enum over. Write-direction only: see <see cref="AnnouncementSource"/>'s
    /// own remarks for why no read-direction counterpart exists yet.</summary>
    static string ToSourceText(AnnouncementSource source) => source switch
    {
        AnnouncementSource.Token => "token",
        AnnouncementSource.Session => "session",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unmapped AnnouncementSource."),
    };

    /// <summary>
    /// <see cref="IAnnouncementStore.InsertOrCollapseAsync"/> — the endpoint's own seam onto
    /// <see cref="InsertAsync"/> (PLAN T339). Maps the Core-level <see cref="AnnouncementSubmitter"/>
    /// the caller derived from the AUTHENTICATED PRINCIPAL to this assembly's own
    /// <see cref="AnnouncementSource"/> (mirrors <see cref="ToSourceText"/>'s own exhaustive, throwing
    /// switch one enum over — <see cref="AnnouncementSubmitter"/>'s own remarks name why the caller
    /// must never derive this from the request body).
    ///
    /// <para>
    /// <b>The 280-char CHECK backstop (T337 review carry-forward).</b> The endpoint validates message
    /// length itself before ever calling this method, so <see cref="CheckViolation"/> should be
    /// unreachable in practice — this catch exists purely so a misconfigured
    /// <c>AnnouncementsOptions.MessageMaxChars</c> (set above the DDL's own fixed 280) degrades to a
    /// declined write (<see langword="null"/>) rather than an unhandled exception surfacing as a raw
    /// 500 that leaks SQL detail to the caller (mirrors <see cref="ShowRepository.CreateAsync"/>'s own
    /// PostgresException-to-typed-outcome mapping, one degenerate case simpler: there is no OTHER
    /// outcome this method's caller needs to distinguish, so a nullable return is enough — the same
    /// "null means declined without contacting further" shape <see cref="ILiquidsoapControl.PushAsync"/>
    /// already establishes for this codebase).
    /// </para>
    /// </summary>
    public async Task<long?> InsertOrCollapseAsync(
        string message, bool verbatim, string? requestedVoice, AnnouncementSubmitter submitter, TimeSpan? ttl, CancellationToken ct)
    {
        try
        {
            return await InsertAsync(message, verbatim, requestedVoice, ToSource(submitter), ttl, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == CheckViolation)
        {
            return null;
        }
    }

    /// <summary>Maps <see cref="AnnouncementSubmitter"/> (Core) to <see cref="AnnouncementSource"/>
    /// (this assembly's own write-direction enum) — see <see cref="InsertOrCollapseAsync"/>'s own
    /// remarks for why the two types exist separately rather than sharing one across the assembly
    /// boundary.</summary>
    static AnnouncementSource ToSource(AnnouncementSubmitter submitter) => submitter switch
    {
        AnnouncementSubmitter.Session => AnnouncementSource.Session,
        AnnouncementSubmitter.Token => AnnouncementSource.Token,
        _ => throw new ArgumentOutOfRangeException(nameof(submitter), submitter, "Unmapped AnnouncementSubmitter."),
    };

    /// <summary><see cref="IAnnouncementStore.CountPendingAsync"/> — every row currently
    /// <c>state = 'pending'</c>, regardless of <c>expires_at</c> (SPEC F143.4's depth cap counts a
    /// row from the moment it's accepted; an expired-but-not-yet-swept row still occupies a depth slot
    /// until PLAN T343's lifecycle guardian reaches it, the same "sweep is a separate concern from the
    /// count" posture <see cref="ExpireStaleAsync"/>'s own total transition already keeps).</summary>
    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*)::int from station.announcement where state = 'pending'",
            cancellationToken: ct));
    }

    /// <summary>
    /// Atomically flips up to <paramref name="n"/> oldest deliverable rows (<c>pending</c>, unexpired
    /// as of <paramref name="now"/>) to <c>claimed</c>, stamping <c>claimed_at</c>/
    /// <c>state_changed_at</c>, and returns them oldest-first (SPEC F144.1's vend order). <c>FOR
    /// UPDATE SKIP LOCKED</c> in the row-selecting CTE makes two concurrent claimers (unlikely at this
    /// station's scale, but free to guarantee) partition the deliverable set rather than double-claim
    /// the same rows — the update that follows only ever touches rows THIS call's own selection locked.
    /// The <c>returning a.*</c>/<c>select *</c> pair below deliberately does not re-spell
    /// <see cref="SelectColumns"/>'s own 13-column list a second time — Dapper maps
    /// <see cref="AnnouncementRow"/>'s constructor parameters BY NAME (<c>MatchNamesWithUnderscores</c>
    /// is enabled globally), so a wildcard projection from the same table binds identically.
    /// </summary>
    public async Task<IReadOnlyList<AnnouncementRow>> ClaimOldestAsync(int n, DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AnnouncementRow>(new CommandDefinition(
            """
            with claimable as (
                select id
                from station.announcement
                where state = 'pending' and expires_at > @Now
                order by created_at asc, id asc
                limit @N
                for update skip locked
            ),
            claimed as (
                update station.announcement a
                set state = 'claimed', claimed_at = now(), state_changed_at = now()
                from claimable c
                where a.id = c.id
                returning a.*
            )
            select * from claimed
            order by created_at asc, id asc
            """,
            new { N = n, Now = now },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// <see cref="IAnnouncementSource.ClaimDeliverableAsync"/> — the SPEC F144.1 vend, mapping this
    /// class's own <see cref="AnnouncementRow"/> shape onto the narrower Core-crossing
    /// <see cref="AnnouncementItem"/> (PLAN T341).
    ///
    /// <para>
    /// <b>The <paramref name="max"/> &lt;= 0 clamp (T338 review carry-forward, reworded T341 review
    /// finding F3 — the prior wording claimed <c>ClaimOldestAsync</c>'s SQL "interpolates" this value,
    /// which overstated the risk: <paramref name="max"/> travels as a bound Dapper PARAMETER
    /// (<c>new { N = n, ... }</c> against <c>limit @N</c>), never string-concatenated into the SQL
    /// text, so there is no injection surface here to guard against).</b> The clamp exists for a
    /// narrower, honest reason: Postgres itself rejects a negative <c>LIMIT</c> value outright — even
    /// arriving as a bound parameter — with 22003 (invalid_row_count_in_limit_clause), rather than
    /// treating it as "none". Every caller today (<c>Orchestrator</c>'s own fixed vend-cap constant)
    /// already passes a small positive value, so this clamp exists purely as this seam's own defensive
    /// floor against a FUTURE caller's mistake, not a workaround for anything reachable today — "vend
    /// at most zero" is a perfectly answerable request (the empty list below), never an exception.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct)
    {
        var clamped = Math.Max(max, 0);
        if (clamped == 0) return [];

        var claimed = await ClaimOldestAsync(clamped, DateTimeOffset.UtcNow, ct);
        return claimed.Select(ToAnnouncementItem).ToArray();
    }

    static AnnouncementItem ToAnnouncementItem(AnnouncementRow row) =>
        new(row.Id, row.Message, row.Verbatim, row.RequestedVoice);

    /// <summary>
    /// <see cref="IAnnouncementLifecycle.MarkAiredAsync"/> — <c>claimed -&gt; aired</c> (SPEC F143.3):
    /// stamped ONLY on a TrackAired observation of the announcement's own segment — never on
    /// push/vend alone (the gh-#612 lesson named in ARCHITECTURE.md). A total, idempotent-safe
    /// transition: a row not currently <c>claimed</c> (already aired, re-armed back to pending, or
    /// unknown) leaves the guarded <c>WHERE</c> matching nothing — this never throws, it reports
    /// <see langword="null"/>.
    ///
    /// <b>Returns the row's own <c>collapse_count</c> (PLAN T343), not a bare success flag.</b> The
    /// booth log's own <c>announcement-aired</c> entry carries this count (SPEC F143.3) — reading it
    /// off the SAME <c>UPDATE ... RETURNING</c> that performs the transition avoids a second round
    /// trip (and the read-after-write race a separate SELECT would open against a row this same
    /// statement just changed).
    /// </summary>
    public async Task<int?> MarkAiredAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            update station.announcement
            set state = 'aired', aired_at = now(), state_changed_at = now()
            where id = @Id and state = 'claimed'
            returning collapse_count
            """,
            new { Id = id },
            cancellationToken: ct));
    }

    /// <summary>
    /// <see cref="IAnnouncementLifecycle.FindClaimedPastGraceAsync"/> — the re-arm sweep's own
    /// candidate read (SPEC F144.5, PLAN T343): every <c>claimed</c> row whose <c>claimed_at</c> is
    /// older than <paramref name="now"/> minus <paramref name="grace"/>. A read only — the caller
    /// (<c>AnnouncementLifecycleGuardianService</c>) drives <see cref="ReArmAsync"/> per candidate
    /// itself, mirroring <see cref="ClaimOldestAsync"/>'s own read-then-transition split one seam
    /// over. Callers MUST run <see cref="ExpireStaleAsync"/> first in the same sweep — see that
    /// member's own remarks and <see cref="IAnnouncementLifecycle.FindClaimedPastGraceAsync"/>'s own
    /// ordering note for why.
    /// </summary>
    public async Task<IReadOnlyList<long>> FindClaimedPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            """
            select id from station.announcement
            where state = 'claimed' and claimed_at < @Threshold
            order by claimed_at asc, id asc
            """,
            new { Threshold = now - grace },
            cancellationToken: ct));
        return ids.AsList();
    }

    /// <summary>
    /// <c>pending|claimed -&gt; declined</c>, stamping <see cref="AnnouncementRow.DeclineReason"/> and
    /// <c>state_changed_at</c> (SPEC F143.2/F145.2 — e.g. the private-&gt;public flip declines every
    /// live row with reason <c>"station went public"</c>). Bulk by design: <paramref name="ids"/> may
    /// name zero, one, or many rows; a row already terminal (aired/expired/declined) is silently
    /// skipped by the guarded <c>WHERE</c>, never re-declined over its own history. Returns the number
    /// of rows actually declined.
    /// </summary>
    public async Task<int> MarkDeclinedAsync(IReadOnlyList<long> ids, string reason, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.announcement
            set state = 'declined', decline_reason = @Reason, state_changed_at = now()
            where id = any(@Ids) and state in ('pending', 'claimed')
            """,
            new { Ids = ids.ToArray(), Reason = reason },
            cancellationToken: ct));
    }

    /// <summary>
    /// <see cref="IAnnouncementLifecycle.DeclineAllLiveAsync"/> — the private→public flip's own bulk
    /// sweep (SPEC F145.2, PLAN T343): every row CURRENTLY <c>pending</c> or <c>claimed</c> declines,
    /// unconditionally, stamping <paramref name="reason"/>. Deliberately not built on
    /// <see cref="MarkDeclinedAsync"/>'s id-list shape — the flip has no candidate list to hand it, so
    /// this is its own single <c>UPDATE ... WHERE state IN (...)</c>, the same "finds its own
    /// candidates" shape <see cref="ExpireStaleAsync"/> already is, rather than a list-then-decline
    /// round trip that would open a window between the two calls. Returns the number of rows declined
    /// (zero is a normal outcome — nothing was live at the moment of the flip).
    /// </summary>
    public async Task<int> DeclineAllLiveAsync(string reason, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.announcement
            set state = 'declined', decline_reason = @Reason, state_changed_at = now()
            where state in ('pending', 'claimed')
            """,
            new { Reason = reason },
            cancellationToken: ct));
    }

    /// <summary>
    /// <c>pending|claimed -&gt; expired</c> for every row whose <c>expires_at</c> is before
    /// <paramref name="now"/> (SPEC F143.2, STORY-357 AC2) — visible, never silent: the row survives at
    /// <c>state = 'expired'</c> with <c>state_changed_at</c> stamped, readable via
    /// <see cref="HistoryAsync"/>. Returns the number of rows expired.
    /// </summary>
    public async Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.announcement
            set state = 'expired', state_changed_at = now()
            where state in ('pending', 'claimed') and expires_at < @Now
            """,
            new { Now = now },
            cancellationToken: ct));
    }

    /// <summary>
    /// <c>claimed -&gt; pending</c> (SPEC F144.5): a claimed announcement whose segment never reached
    /// air within claim + one break cycle re-arms, clearing <c>claimed_at</c> so it is deliverable
    /// again via <see cref="ClaimOldestAsync"/> — <c>expires_at</c> is untouched, so a re-armed row
    /// whose TTL has since passed is <see cref="ExpireStaleAsync"/>'s to catch next, not this method's
    /// (the caller decides which applies — SPEC F144.5's own "TTL permitting" clause). Total: a row not
    /// currently <c>claimed</c> leaves the guard matching nothing and reports <see langword="false"/>.
    /// </summary>
    public async Task<bool> ReArmAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.announcement
            set state = 'pending', claimed_at = null, state_changed_at = now()
            where id = @Id and state = 'claimed'
            """,
            new { Id = id },
            cancellationToken: ct));
        return affected == 1;
    }

    /// <summary>
    /// Newest-first rows across every state (SPEC F146.2's history surface) — the visible-decline/
    /// visible-expiry law's own read side: nothing this store transitions is ever hidden, only listed.
    /// The concrete, <see cref="AnnouncementRow"/>-returning shape — <see cref="GenWave.MediaLibrary.Tests"/>'s
    /// own Story357_AnnouncementStore.cs drives this directly against a real Postgres fixture (T337);
    /// <see cref="IAnnouncementStore.HistoryAsync"/>'s explicit interface implementation immediately
    /// below (PLAN T344) is a thin, mapping-only wrapper over this same method — the
    /// <see cref="ClaimDeliverableAsync"/>/<see cref="ClaimOldestAsync"/> shape one seam over — rather
    /// than a second name: <see cref="AnnouncementRow"/> is internal to this assembly, so the
    /// Core-crossing seam needs its own return type (<see cref="AnnouncementHistoryEntry"/>), and C#
    /// has no way to overload solely on return type — an explicit interface implementation is the
    /// idiomatic way to keep both this method's own existing name AND the interface member's identical
    /// name on one class without a collision.
    /// </summary>
    public async Task<IReadOnlyList<AnnouncementRow>> HistoryAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AnnouncementRow>(new CommandDefinition(
            $"{SelectColumns} order by created_at desc, id desc limit @Limit",
            new { Limit = limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// <see cref="IAnnouncementStore.HistoryAsync"/> (PLAN T344) — maps this class's own
    /// <see cref="AnnouncementRow"/> rows onto the narrower Core-crossing
    /// <see cref="AnnouncementHistoryEntry"/>, mirroring <see cref="ClaimDeliverableAsync"/>'s own
    /// "thin wrapper, explicit conversion at the boundary" shape.
    /// </summary>
    async Task<IReadOnlyList<AnnouncementHistoryEntry>> IAnnouncementStore.HistoryAsync(int limit, CancellationToken ct)
    {
        var rows = await HistoryAsync(limit, ct);
        return rows.Select(ToHistoryEntry).ToArray();
    }

    static AnnouncementHistoryEntry ToHistoryEntry(AnnouncementRow row) => new(
        row.Id, row.Message, row.Verbatim, ToStateText(row.State), row.DeclineReason, row.CollapseCount,
        row.CreatedAt, row.ExpiresAt, row.AiredAt);

    /// <summary>Maps <see cref="AnnouncementState"/> to <c>station.announcement.state</c>'s own
    /// lowercase text values for the wire (PLAN T344) — mirrors <see cref="ToSourceText"/>'s own
    /// exhaustive, throwing switch immediately above, one column over. A DELIBERATE second copy of
    /// <see cref="AnnouncementStateTypeHandler"/>'s own identical switch, not a shared call: that
    /// type's mapping is Dapper-parameter plumbing (a SQL boundary concern), this one is a wire-DTO
    /// boundary concern — two different reasons to convert the same enum, at two different seams,
    /// the same restraint this file's own remarks already apply to <see cref="ToSourceText"/>/
    /// <see cref="ToSource"/>.
    ///
    /// <para>
    /// <b>Internal, not private (T344 review finding F1).</b> The one exception to this file's own
    /// private-mapper convention: <c>GenWave.MediaLibrary.Tests</c>' own
    /// <c>FeatureAnnouncementStateWireParity</c> calls this directly to pin its five outputs against
    /// <c>admin-ui/lib/announcements-api.ts</c>'s <c>AnnouncementState</c> union literal — the same
    /// "internal, reachable only via the test project's own InternalsVisibleTo grant" shape
    /// <see cref="LegacyPersonaCardMapper.Slugify"/> already establishes for
    /// <c>FeaturePersonaSlugParity</c>, one seam over.
    /// </para>
    /// </summary>
    internal static string ToStateText(AnnouncementState state) => state switch
    {
        AnnouncementState.Pending => "pending",
        AnnouncementState.Claimed => "claimed",
        AnnouncementState.Aired => "aired",
        AnnouncementState.Expired => "expired",
        AnnouncementState.Declined => "declined",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped AnnouncementState."),
    };
}
