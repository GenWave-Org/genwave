using System.Text.Json;
using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// <see cref="IMediaRotationSink"/>'s one implementation (SPEC F149.1-F149.3, STORY-367, PLAN T355,
/// gh-#529) over <c>library.media_rotation</c> — the same 1:1-extension-table shape
/// <see cref="Catalog.MediaRatingRepository"/> (SPEC F33) already established for
/// <c>library.media_rating</c>: connection-per-call against the library's own
/// <see cref="NpgsqlDataSource"/>, PK = FK, a write here never bumps <c>library.media</c>'s own
/// <c>xmin</c> (postgres-dba Rule-2 deviation, pinned by db/41's own header remarks and STORY-367
/// AC3).
///
/// <para>
/// <b>gh-#99 safe-scope exclusion, applied HERE rather than at the caller</b> — the same carve-out
/// <see cref="Catalog.MediaRatingRepository"/> already owns for votes/never-play, via
/// <paramref name="safeScope"/> (<see cref="ISafeScopeProvider"/>, re-read live on every call). A safe
/// loop row (the seeded safe loop, authored safe segments, station IDs) shares
/// <c>MediaRotationEventSink</c>'s own null-<c>SegmentKind</c>/numeric-id shape with genuine music — that
/// hot-path sink cannot perform the async membership read needed to tell them apart (see its own
/// remarks) — so <see cref="RecordAiringAsync"/> and <see cref="GetNeverAiredCountAsync"/> both apply
/// the exclusion themselves: a safe-loop airing must never inflate "Please Stand By"'s own
/// <c>play_count</c>, and a safe-loop row must never count toward the never-aired figure either.
/// </para>
///
/// <para>
/// <b><see cref="GetRotationSinceAsync"/>/<see cref="GetNeverAiredCountAsync"/> read a SECOND,
/// unrelated store</b> — <c>station.settings</c>' <c>Gardener:RotationSince</c> row, stamped once
/// by db/41's own migration seed (SPEC F149.3) — through <paramref name="stationSettings"/>
/// (<see cref="StationSettingsRepository"/>), never a raw query against THIS class's own library
/// connection: <c>library_svc</c> has no grant into the <c>station</c> schema (db/41's own header;
/// see <c>GenWave.Host.Auth.AnnounceTokenStore</c>/<c>GenWave.Host.Seeding.SafeLoopSeedMarkerStore</c>'s
/// identical cross-schema-boundary remarks for the sibling precedent), so the two reads genuinely
/// use two different Postgres roles/connections even though they are one C# class for cohesion —
/// every consumer of "the rotation ledger" reaches it through this ONE type.
/// </para>
///
/// <para>
/// <b>Deliberately outside <c>StationSettingsAllowlist</c>/<c>IStationSettingsStore</c></b> — the
/// <c>SafeLoopSeedMarkerStore</c>/<c>AnnounceTokenStore</c> precedent (F27.10):
/// <c>Gardener:RotationSince</c> is a migration-stamped epoch, never operator-editable, and must
/// never appear on <c>GET</c>/<c>PUT /api/settings</c>.
/// </para>
/// </summary>
sealed class MediaRotationRepository(
    NpgsqlDataSource dataSource, StationSettingsRepository stationSettings, ISafeScopeProvider safeScope)
    : IMediaRotationSink
{
    /// <summary>
    /// The settings key <see cref="GetRotationSinceAsync"/> reads. Outside the
    /// <c>Station:*</c>/<c>Announcements:*</c> allowlisted namespace by construction — see this
    /// class's own remarks for why it must never be allowlisted.
    /// </summary>
    public const string RotationSinceKey = "Gardener:RotationSince";

    /// <summary>
    /// The gh-#99 safe-scope carve-out, shared by every read/write below that needs it
    /// (<see cref="RecordAiringAsync"/>, <see cref="GetNeverAiredCountAsync"/>,
    /// <see cref="GetRotationHealthAsync"/>): appends the <c>and not (m.library_id = any(...))</c>
    /// predicate and its parameter onto <paramref name="parameters"/> ONLY when
    /// <paramref name="scope"/> is non-empty. An empty safe scope short-circuits to no extra predicate
    /// at all — never <c>any('{}')</c>, which would silently drop every row (mirrors
    /// <c>MediaRatingRepository.ExcludeSafeContent</c>'s identical short-circuit; PLAN T371
    /// carry-forward (a) pins this branch with a fact of its own).
    /// </summary>
    static string AppendSafeExclusion(DynamicParameters parameters, LibraryScope scope)
    {
        if (scope.IsEmpty) return "";
        parameters.Add("safeLibraryIds", scope.LibraryIds.ToArray());
        return " and not (m.library_id = any(@safeLibraryIds))";
    }

    /// <summary>
    /// gh-#99 — the upsert is an <c>INSERT ... SELECT</c> off <c>library.media</c> rather than a bare
    /// <c>INSERT ... VALUES</c> (<see cref="Catalog.MediaRatingRepository.VoteAsync"/>'s own shape):
    /// the SELECT's <c>WHERE</c> is where the safe-scope carve-out lives, so a safe-loop
    /// <paramref name="mediaId"/> matches zero rows and the whole statement becomes a no-op — no row is
    /// inserted, no existing row is updated, exactly as if <see cref="RecordAiringAsync"/> had never been
    /// called for it. <see cref="ISafeScopeProvider.Current"/> is read fresh on every call (never
    /// cached), so a live SafeScope edit governs the very next airing. An empty safe scope short-circuits
    /// to the pre-#99 shape: no extra predicate, no extra parameter (mirrors
    /// <c>MediaRatingRepository.ExcludeSafeContent</c>'s identical short-circuit).
    ///
    /// <para>
    /// <b>T365 review HIGH-2 fix</b>: <c>first_aired_at = coalesce(library.media_rotation.first_aired_at,
    /// excluded.first_aired_at)</c> on the DO UPDATE branch — since T365, <c>MediaThumbRepository</c> can
    /// also be the FIRST writer of a <c>library.media_rotation</c> row (a thumb on a never-aired track),
    /// always with <c>first_aired_at</c> NULL by construction. Before this fix, this method's own DO
    /// UPDATE never touched that column at all, so a thumbed-then-aired track's row stayed permanently
    /// NULL on <c>first_aired_at</c> despite a nonzero <c>play_count</c>. <c>coalesce(existing, new)</c>
    /// sets it exactly once — on whichever call is the row's TRUE first airing — and never overwrites an
    /// already-stamped value on every airing after that.
    /// </para>
    /// </summary>
    public async Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("mediaId", mediaId);
        parameters.Add("airedAt", airedAt);
        var safeExclusion = AppendSafeExclusion(parameters, safeScope.Current);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.media_rotation (media_id, play_count, first_aired_at, last_aired_at)
            select @mediaId, 1, @airedAt, @airedAt
            from library.media m
            where m.id = @mediaId{safeExclusion}
            on conflict (media_id) do update
              set play_count     = library.media_rotation.play_count + 1,
                  first_aired_at = coalesce(library.media_rotation.first_aired_at, excluded.first_aired_at),
                  last_aired_at  = excluded.last_aired_at,
                  updated_at     = now()
            """,
            parameters,
            cancellationToken: ct));
    }

    /// <summary>
    /// The migration epoch (SPEC F149.3) — <see langword="null"/> only if db/41's own seed step has
    /// never run against this station (a pre-Gardener install). <see cref="StationSettingsRepository.ReadValueAsync"/>'s
    /// own JSON-scalar shape (db/41: <c>to_jsonb(now())</c> renders as an ISO-8601 string) round-trips
    /// straight into <see cref="DateTimeOffset"/>.
    /// </summary>
    public async Task<DateTimeOffset?> GetRotationSinceAsync(CancellationToken ct)
    {
        var stored = await stationSettings.ReadValueAsync(RotationSinceKey, ct);
        return stored is null ? null : JsonSerializer.Deserialize<DateTimeOffset>(stored);
    }

    /// <summary>
    /// The F149.5 "playable rows with no ledger row or play_count 0" never-aired count (SPEC
    /// F149.3/F149.5, STORY-367 AC7), read beside <see cref="GetRotationSinceAsync"/>'s epoch. Scoped
    /// to <see cref="MediaRepository.PlayablePredicate"/> (T374 review ADVISORY: <c>internal</c>,
    /// referenced directly rather than mirrored — db/41's own <c>find_near_duplicates</c> function
    /// still mirrors the same text for the identical reason, since SQL functions cannot reference a
    /// C# constant) — an unavailable, ineligible, or never-play row is not "waiting to air", so it
    /// must not inflate this figure. The gh-#99
    /// safe-scope exclusion applies here too, the same short-circuiting way
    /// <see cref="RecordAiringAsync"/> applies it.
    /// </summary>
    public async Task<long> GetNeverAiredCountAsync(CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var safeExclusion = AppendSafeExclusion(parameters, safeScope.Current);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
            select count(*)
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            left join library.media_rotation rot on rot.media_id = m.id
            where {MediaRepository.PlayablePredicate}
              and (rot.media_id is null or rot.play_count = 0){safeExclusion}
            """,
            parameters,
            cancellationToken: ct));
    }

    /// <summary>
    /// The SPEC F149.5 dashboard/catalog aggregate (STORY-368, PLAN T371): one grouped query —
    /// <c>count(*) filter (where ...)</c>, mirroring <c>MediaRepository.GetStatusCountsAsync</c>'s own
    /// shape — produces <see cref="RotationHealth.Playable"/> (a bare <c>count(*)</c> over the SAME
    /// WHERE the filtered counts share — every row this query touches at all is, by construction,
    /// playable-and-in-scope) alongside <see cref="RotationHealth.NeverAired"/>/
    /// <see cref="RotationHealth.AiredOnce"/>/<see cref="RotationHealth.NotAiredDays90"/> in one table
    /// scan, plus <see cref="RotationHealth.RotationSince"/> from <see cref="GetRotationSinceAsync"/>
    /// (a second, unrelated store — see that method's own remarks). <see cref="MediaRepository.PlayablePredicate"/>
    /// and the gh-#99 safe-scope exclusion apply exactly as <see cref="GetNeverAiredCountAsync"/>
    /// applies them; the ONE addition here is <c>m.library_id = any(@libraryIds)</c> — the station's
    /// own rotation <paramref name="scope"/>, needing no empty-scope special case: <c>= any('{}')</c>
    /// matches nothing, so every count reads 0 naturally (the <c>GetStatusCountsAsync</c> precedent).
    /// <c>NotAiredDays90</c> never double-counts a never-aired row: <c>rot.last_aired_at</c> is
    /// <see langword="null"/> for one, and <c>null &lt; now() − 90 days</c> is never true in SQL.
    /// </summary>
    public async Task<RotationHealth> GetRotationHealthAsync(LibraryScope scope, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("libraryIds", scope.LibraryIds.ToArray());
        var safeExclusion = AppendSafeExclusion(parameters, safeScope.Current);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var counts = await conn.QuerySingleAsync<RotationHealthCountsRow>(
            new CommandDefinition(
                $"""
                select
                  count(*)::bigint as playable,
                  count(*) filter (where rot.media_id is null or rot.play_count = 0)::bigint as never_aired,
                  count(*) filter (where rot.play_count = 1)::bigint as aired_once,
                  count(*) filter (where rot.last_aired_at < now() - interval '90 days')::bigint as not_aired_days90
                from library.media m
                left join library.media_rating r on r.media_id = m.id
                left join library.media_rotation rot on rot.media_id = m.id
                where {MediaRepository.PlayablePredicate}
                  and m.library_id = any(@libraryIds){safeExclusion}
                """,
                parameters,
                cancellationToken: ct));

        var since = await GetRotationSinceAsync(ct);
        return new RotationHealth(counts.Playable, counts.NeverAired, counts.AiredOnce, counts.NotAiredDays90, since);
    }
}
