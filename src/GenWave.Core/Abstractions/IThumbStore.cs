using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The Library Gardener's taste-thumb seam (SPEC F150.1, F150.7, F150.9; STORY-371, STORY-369; PLAN
/// T365, gh-#529) — the same "Core-level port a MediaLibrary repository implements directly" home
/// <see cref="IMediaRotationSink"/>'s own remarks establish one seam over: no third-party type
/// appears anywhere in this signature, so it belongs here rather than in <c>GenWave.MediaLibrary</c>,
/// even though <c>GenWave.MediaLibrary.Garden.MediaThumbRepository</c> (its one implementation) is
/// the only thing that ever calls Npgsql/Dapper to satisfy it.
///
/// <para>
/// <b>F150.1's own disjointness</b>: a thumb writes ONLY <c>library.media_thumb</c> +
/// <c>library.media_rotation</c> — never <c>library.media_rating</c>, never <c>persona_taste</c>.
/// <see cref="RecordAsync"/> is idempotent on <c>(media_id, airing_started_at, listener_key)</c>
/// (F150.7): a repeat of the SAME direction is <see cref="ThumbWriteResult.Unchanged"/>, a change of
/// direction re-aggregates the nudge and returns <see cref="ThumbWriteResult.Flipped"/>. A row on a
/// <c>Station:SafeScope:LibraryIds</c> library (gh-#99) or an unknown media id both return
/// <see cref="ThumbWriteResult.Ignored"/> — the Host controller (T366) answers with the SAME constant
/// 202 for every outcome, never distinguishing them on the wire.
/// </para>
///
/// <para>
/// <b><see cref="SweepAsync"/></b> is the F150.9 retention sweep: <c>library.media_thumb</c> rows
/// older than <c>GardenerOptions.ThumbRetentionDays</c> are deleted, but the lifetime
/// <c>thumbs_up</c>/<c>thumbs_down</c> counters and the last-computed <c>nudge</c> on
/// <c>library.media_rotation</c> survive untouched — the sweep never re-aggregates.
/// </para>
///
/// <para>
/// <b><see cref="RecomputeAllAsync"/></b> is the gardener's hourly decay pass (F150.9): a thumb's
/// contribution to <c>nudge</c> decays continuously with age, so even a media row that received no
/// NEW thumb this hour needs its <c>nudge</c> re-computed against the current instant to stay honest
/// — this is what keeps that decay moving between writes, the same formula
/// <see cref="RecordAsync"/> applies on every write.
/// </para>
/// </summary>
public interface IThumbStore
{
    /// <summary>
    /// Records (or flips) a single thumb (SPEC F150.7). <paramref name="listenerKey"/> is the
    /// caller's own idempotency identity — <c>sha256(cookie token)</c> for a spectator, the literal
    /// <c>"operator"</c> for <paramref name="source"/> <see cref="ThumbSource.Operator"/> — never
    /// logged, never returned. Implementations MUST apply the gh-#99 safe-scope exclusion themselves
    /// (mirrors <see cref="IMediaRotationSink.RecordAiringAsync"/>'s own contract): a thumb on a
    /// safe-scope row is never meaningful and must return <see cref="ThumbWriteResult.Ignored"/>,
    /// never throw.
    /// </summary>
    Task<ThumbWriteResult> RecordAsync(
        long mediaId, DateTimeOffset airingStartedAt, string listenerKey,
        ThumbDirection direction, ThumbSource source, CancellationToken ct);

    /// <summary>
    /// The F150.5 PER-LISTENER daily-cap read (STORY-369, PLAN T366): how many
    /// <c>library.media_thumb</c> rows this <paramref name="listenerKey"/> has accrued since
    /// <paramref name="since"/> — a plain count over the same table <see cref="RecordAsync"/>
    /// writes, exact and restart-safe (unlike an in-memory window, this survives an api restart
    /// honestly). The caller (<c>SpectatorThumbsController</c>) compares the result against
    /// <c>GardenerOptions.ThumbDailyCap</c> BEFORE calling <see cref="RecordAsync"/> — this method
    /// itself applies no cap, no <see cref="ISafeScopeProvider"/> exclusion, and never throws for an
    /// unknown key (an unrecognised or never-seen <paramref name="listenerKey"/> simply counts zero).
    /// </summary>
    Task<int> CountByListenerSinceAsync(string listenerKey, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Deletes every <c>library.media_thumb</c> row older than
    /// <c>GardenerOptions.ThumbRetentionDays</c> (SPEC F150.9); returns the row count deleted.
    /// <c>library.media_rotation.thumbs_up</c>/<c>thumbs_down</c>/<c>nudge</c> are untouched — this
    /// method never re-aggregates.
    /// </summary>
    Task<int> SweepAsync(CancellationToken ct);

    /// <summary>
    /// The gardener's hourly decay pass (SPEC F150.9): re-computes <c>nudge</c> for every media id
    /// that carries at least one <c>library.media_thumb</c> row, applying the same
    /// exponential-half-life/saturation formula <see cref="RecordAsync"/> applies on write.
    /// </summary>
    Task RecomputeAllAsync(CancellationToken ct);
}
