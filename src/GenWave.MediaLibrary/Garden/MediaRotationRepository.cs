using System.Text.Json;
using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
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
    /// gh-#99 — the upsert is an <c>INSERT ... SELECT</c> off <c>library.media</c> rather than a bare
    /// <c>INSERT ... VALUES</c> (<see cref="Catalog.MediaRatingRepository.VoteAsync"/>'s own shape):
    /// the SELECT's <c>WHERE</c> is where the safe-scope carve-out lives, so a safe-loop
    /// <paramref name="mediaId"/> matches zero rows and the whole statement becomes a no-op — no row is
    /// inserted, no existing row is updated, exactly as if <see cref="RecordAiringAsync"/> had never been
    /// called for it. <see cref="ISafeScopeProvider.Current"/> is read fresh on every call (never
    /// cached), so a live SafeScope edit governs the very next airing. An empty safe scope short-circuits
    /// to the pre-#99 shape: no extra predicate, no extra parameter (mirrors
    /// <c>MediaRatingRepository.ExcludeSafeContent</c>'s identical short-circuit).
    /// </summary>
    public async Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct)
    {
        var scope = safeScope.Current;
        var parameters = new DynamicParameters();
        parameters.Add("mediaId", mediaId);
        parameters.Add("airedAt", airedAt);

        var safeExclusion = "";
        if (!scope.IsEmpty)
        {
            parameters.Add("safeLibraryIds", scope.LibraryIds.ToArray());
            safeExclusion = " and not (m.library_id = any(@safeLibraryIds))";
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.media_rotation (media_id, play_count, first_aired_at, last_aired_at)
            select @mediaId, 1, @airedAt, @airedAt
            from library.media m
            where m.id = @mediaId{safeExclusion}
            on conflict (media_id) do update
              set play_count    = library.media_rotation.play_count + 1,
                  last_aired_at = excluded.last_aired_at,
                  updated_at    = now()
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
    /// to <c>MediaRepository.PlayablePredicate</c>'s own text — <c>m.state = 'ready' and m.measurable
    /// and m.eligible and not coalesce(r.never_play, false)</c>, mirrored verbatim here (that constant
    /// is <c>private</c> to <c>Catalog.MediaRepository</c>; db/41's own <c>find_near_duplicates</c>
    /// function mirrors the exact same text for the identical reason) — an unavailable, ineligible, or
    /// never-play row is not "waiting to air", so it must not inflate this figure. The gh-#99
    /// safe-scope exclusion applies here too, the same short-circuiting way
    /// <see cref="RecordAiringAsync"/> applies it.
    /// </summary>
    public async Task<long> GetNeverAiredCountAsync(CancellationToken ct)
    {
        var scope = safeScope.Current;
        var parameters = new DynamicParameters();

        var safeExclusion = "";
        if (!scope.IsEmpty)
        {
            parameters.Add("safeLibraryIds", scope.LibraryIds.ToArray());
            safeExclusion = " and not (m.library_id = any(@safeLibraryIds))";
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
            select count(*)
            from library.media m
            left join library.media_rating r on r.media_id = m.id
            left join library.media_rotation rot on rot.media_id = m.id
            where m.state = 'ready' and m.measurable and m.eligible and not coalesce(r.never_play, false)
              and (rot.media_id is null or rot.play_count = 0){safeExclusion}
            """,
            parameters,
            cancellationToken: ct));
    }
}
