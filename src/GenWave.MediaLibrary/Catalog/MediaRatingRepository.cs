using System.Globalization;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The in-process implementation of <see cref="IMediaRating"/> (SPEC F33, STORY-110) over
/// <c>library.media_rating</c> — a 1:1 extension table kept deliberately separate from
/// <c>library.media</c> so a vote or never-play set never bumps that row's <c>xmin</c> (F33.1).
/// Connection-per-query against the library's own <see cref="NpgsqlDataSource"/>, mirroring
/// <see cref="MediaRepository"/>'s wiring — singleton-safe with no captive dependency. No
/// <see cref="LibraryScope"/> gating anywhere (F33.5): rating is a per-row concern, not a
/// rotation-scope one.
/// </summary>
sealed class MediaRatingRepository(NpgsqlDataSource dataSource, IStationEventSink? events = null) : IMediaRating
{
    // MediaMutated publish seam for rating writes (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    // Postgres SQLSTATE code for foreign-key violation — mirrors MediaRepository/AdminLibraryRepository.
    const string ForeignKeyViolation = "23503";

    /// <summary>
    /// Single-statement lazy upsert (F33.3): the INSERT ... ON CONFLICT clamps in both the initial
    /// value (<c>50 + delta</c>) and the update branch (<c>score + delta</c>), so a first-ever vote
    /// and a hundredth vote go through the exact same round trip. The insert IS the existence check —
    /// a missing media row raises a 23503 foreign-key violation on the implicit
    /// <c>media_id -> library.media(id)</c> FK, which is caught here rather than pre-checked with a
    /// SELECT (avoids a TOCTOU gap and a wasted round trip on the common path).
    /// </summary>
    public async Task<VoteOutcome> VoteAsync(string mediaId, VoteDirection direction, CancellationToken ct)
    {
        if (!long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return new VoteOutcome(RatingWriteResult.NotFound, null);

        var delta = direction == VoteDirection.Up ? 1 : -1;

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var score = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
                insert into library.media_rating (media_id, score)
                values (@id, least(100, greatest(0, 50 + @delta)))
                on conflict (media_id) do update
                  set score = least(100, greatest(0, library.media_rating.score + @delta)),
                      updated_at = now()
                returning score
                """,
                new { id, delta },
                cancellationToken: ct));

            events.Publish(new MediaMutated("rating", id, 1));
            return new VoteOutcome(RatingWriteResult.Updated, score);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return new VoteOutcome(RatingWriteResult.NotFound, null);
        }
    }

    /// <summary>
    /// Idempotent upsert set (F33.4): the UPDATE branch always writes the caller's
    /// <paramref name="neverPlay"/> value regardless of what is already stored, so repeat sets of the
    /// same value are safe no-ops and there is nothing to conflict on. Same FK-violation-as-existence-
    /// check shape as <see cref="VoteAsync"/>. The INSERT column list omits <c>score</c> so a
    /// flag-only write on an unrated row takes the column default (50) rather than disturbing it.
    /// </summary>
    public async Task<NeverPlayOutcome> SetNeverPlayAsync(string mediaId, bool neverPlay, CancellationToken ct)
    {
        if (!long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return new NeverPlayOutcome(RatingWriteResult.NotFound, null);

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var value = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
                insert into library.media_rating (media_id, never_play)
                values (@id, @neverPlay)
                on conflict (media_id) do update
                  set never_play = @neverPlay,
                      updated_at = now()
                returning never_play
                """,
                new { id, neverPlay },
                cancellationToken: ct));

            events.Publish(new MediaMutated("never-play", id, 1));
            return new NeverPlayOutcome(RatingWriteResult.Updated, value);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return new NeverPlayOutcome(RatingWriteResult.NotFound, null);
        }
    }

    /// <summary>
    /// Batch read (F33.2, F33.9): a direct SELECT against <c>library.media_rating</c> with no JOIN
    /// back to <c>library.media</c> — existence is deliberately never re-verified here, since that
    /// would be a read amplification with no consumer value (the UI only ever asks about ids it just
    /// saw from the catalog). Ids that fail to parse (e.g. <c>tts:*</c>) are silently skipped per
    /// F33.9. Every id that does parse yields exactly one entry: a real row's values if one exists,
    /// otherwise the ledger default (score 50, never-play false) applied client-side.
    /// </summary>
    public async Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct)
    {
        var parsed = new List<(string Original, long Id)>(mediaIds.Count);
        foreach (var mediaId in mediaIds)
            if (long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                parsed.Add((mediaId, id));

        if (parsed.Count == 0)
            return [];

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long MediaId, int Score, bool NeverPlay)>(new CommandDefinition(
            "select media_id, score, never_play from library.media_rating where media_id = any(@ids)",
            new { ids = parsed.Select(p => p.Id).Distinct().ToArray() },
            cancellationToken: ct));

        var byId = rows.ToDictionary(r => r.MediaId, r => (r.Score, r.NeverPlay));

        var results = new List<MediaRating>(parsed.Count);
        foreach (var (original, id) in parsed)
        {
            var (score, neverPlay) = byId.TryGetValue(id, out var found) ? found : (50, false);
            results.Add(new MediaRating(original, score, neverPlay));
        }

        return results;
    }

    /// <summary>
    /// Bulk vote (SPEC F61.1, F61.2, STORY-158): one <c>INSERT … SELECT … ON CONFLICT DO UPDATE</c>
    /// fans <see cref="VoteAsync"/>'s exact clamp expression out over every <c>library.media</c> row
    /// <see cref="MediaRepository.BuildAdminWhere"/> matches — the SELECT supplies the matched id set,
    /// the INSERT lazy-upserts each one at once. A row with no existing <c>library.media_rating</c> row
    /// is inserted at <c>50 + delta</c> (clamped); a row that already has one is updated at
    /// <c>score + delta</c> (clamped) — the same per-row math as the single-row path, just batched.
    /// Postgres reports both inserted and conflict-updated rows in the command's affected-row count, so
    /// the returned count equals the number of matched rows, matching what
    /// <see cref="MediaRepository.ListAdminAsync"/> would preview for the same filter (F61.1's "one
    /// shared WHERE builder" — preview and sweep agree). Never touches <c>library.media</c> itself, so
    /// its <c>xmin</c> is untouched (F33.1). Default-deny: an empty scope short-circuits to 0, no SQL.
    /// </summary>
    public async Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct)
    {
        if (scope.IsEmpty) return 0;

        var (where, filterParams) = MediaRepository.BuildAdminWhere(filter, scope);
        var delta = direction == VoteDirection.Up ? 1 : -1;
        filterParams.Add("delta", delta);

        var sql = $"""
            insert into library.media_rating (media_id, score)
            select id, least(100, greatest(0, 50 + @delta))
            from library.media
            where {where}
            on conflict (media_id) do update
              set score = least(100, greatest(0, library.media_rating.score + @delta)),
                  updated_at = now()
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, filterParams, cancellationToken: ct));
        if (affected > 0)
            events.Publish(new MediaMutated("rating-bulk", null, affected));
        return affected;
    }

    /// <summary>
    /// Bulk never-play (SPEC F61.1, F61.2, STORY-158): the same INSERT…SELECT…ON CONFLICT shape as
    /// <see cref="BulkVoteAsync"/>, fanning <see cref="SetNeverPlayAsync"/>'s idempotent set out over
    /// every matched row. The UPDATE branch always writes <paramref name="neverPlay"/> regardless of
    /// the row's current value — a repeat sweep at the same value is a safe no-op, and a later sweep
    /// with the opposite value restores every matched row (never a one-way door). The INSERT column
    /// list omits <c>score</c> so a flag-only write on an unrated row takes the column default (50)
    /// rather than disturbing it. Never touches <c>library.media</c> (F33.1). Default-deny: an empty
    /// scope short-circuits to 0, no SQL.
    /// </summary>
    public async Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct)
    {
        if (scope.IsEmpty) return 0;

        var (where, filterParams) = MediaRepository.BuildAdminWhere(filter, scope);
        filterParams.Add("neverPlay", neverPlay);

        var sql = $"""
            insert into library.media_rating (media_id, never_play)
            select id, @neverPlay
            from library.media
            where {where}
            on conflict (media_id) do update
              set never_play = @neverPlay,
                  updated_at = now()
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, filterParams, cancellationToken: ct));
        if (affected > 0)
            events.Publish(new MediaMutated("never-play-bulk", null, affected));
        return affected;
    }
}
