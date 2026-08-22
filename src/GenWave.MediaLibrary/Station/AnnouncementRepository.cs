using Dapper;
using Npgsql;

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
/// <b>No <c>GenWave.Core.Abstractions</c> seam yet.</b> T337 has no ordering dependency on T338
/// (<c>parallel-group: pg-hv-a</c>) and T338's own <c>IAnnouncementSource</c> is a narrower, vend-only
/// Core seam a Host-side adapter implements OVER this repository (PLAN T341) — not this repository
/// itself. This class therefore stays internal to this assembly for now, the same "ships dark, first
/// consumer lands later" shape <see cref="ShowRepository"/>/<see cref="ThemeRepository"/> originally
/// shipped under.
/// </para>
/// </summary>
sealed class AnnouncementRepository(Lazy<NpgsqlDataSource> dataSource)
{
    /// <summary>The endpoint-facing default TTL (SPEC F143.1) — 15 minutes. A caller that knows a
    /// bounded override (60-3600s, the endpoint's own job to enforce) passes it explicitly; omitting
    /// <c>ttl</c> on <see cref="InsertAsync"/> gets this value.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(900);

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
    /// <c>claimed -&gt; aired</c> (SPEC F143.3): stamped ONLY on a TrackAired observation of the
    /// announcement's own segment — never on push/vend alone (the gh-#612 lesson named in
    /// ARCHITECTURE.md). A total, idempotent-safe transition: a row not currently <c>claimed</c> (already
    /// aired, re-armed back to pending, or unknown) leaves the guarded <c>WHERE</c> matching nothing —
    /// this never throws, it reports <see langword="false"/>.
    /// </summary>
    public async Task<bool> MarkAiredAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.announcement
            set state = 'aired', aired_at = now(), state_changed_at = now()
            where id = @Id and state = 'claimed'
            """,
            new { Id = id },
            cancellationToken: ct));
        return affected == 1;
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
}
