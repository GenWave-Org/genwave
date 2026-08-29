namespace GenWave.Core.Abstractions;

/// <summary>
/// The Library Gardener's own rotation-ledger seam (SPEC F149.1-F149.3, STORY-367, PLAN T355;
/// gh-#529) — the same "Core-level port a MediaLibrary repository implements directly" placement
/// <see cref="IAnnouncementLifecycle"/>'s own remarks already establish one seam over (gh-#400): no
/// third-party type appears anywhere in this signature, so it belongs here rather than in
/// <c>GenWave.MediaLibrary</c>, even though <c>MediaRotationRepository</c> (its one implementation)
/// is the only thing that ever calls Npgsql/Dapper to satisfy it.
///
/// <para>
/// <b><see cref="RecordAiringAsync"/></b> is the write half a Host-side <c>IStationEventSink</c>
/// member (<c>GenWave.Host.Playout.MediaRotationEventSink</c>, mirroring
/// <c>AnnouncementAiredEventSink</c>'s own queue/drain shape one seam over) calls for every
/// null-<c>SegmentKind</c> <c>TrackAired</c> — never idents/patter/crosstalk/announcements (SPEC
/// F149.2's own "byte-identical" test; the discrimination is reused verbatim from
/// <c>BoothLogWriter</c>/<c>AnnouncementAiredEventSink</c>, never re-invented here). That SegmentKind
/// filter alone is NOT "genuine music only", though — the gh-#99 safe loop shares the exact same
/// null-<c>SegmentKind</c>/numeric-id shape, so this interface's own implementation carries the
/// second, async half of the discrimination (see <see cref="RecordAiringAsync"/>'s own remarks).
/// </para>
///
/// <para>
/// <b><see cref="GetRotationSinceAsync"/>/<see cref="GetNeverAiredCountAsync"/></b> are the read
/// half (STORY-367 AC7; SPEC F149.3's "returned beside every never-aired figure" — the same figure
/// SPEC F149.5's later dashboard tile narrows further). Bundled onto this ONE interface rather than
/// split into a second port — the <see cref="IAnnouncementLifecycle"/> precedent again: one Core
/// seam per durable store, even when different Host-side callers only ever touch a subset of its
/// members.
/// </para>
/// </summary>
public interface IMediaRotationSink
{
    /// <summary>
    /// Upserts <c>library.media_rotation</c> for <paramref name="mediaId"/>: <c>play_count + 1</c>,
    /// <c>first_aired_at</c> set only on the row's first-ever airing, <c>last_aired_at =
    /// airedAt</c>. Never touches <c>library.media</c> itself — that row's own <c>xmin</c> MUST
    /// survive an airing (F149.1, STORY-367 AC3). The caller (<c>MediaRotationEventSink</c>) filters
    /// out every idents/patter/crosstalk/announcement <c>TrackAired</c> by its non-null
    /// <c>SegmentKind</c> (STORY-367 AC4), but that cheap synchronous filter CANNOT tell a genuine
    /// music row apart from the gh-#99 safe-loop's own airing — <c>GET /internal/safe-track</c> serves
    /// a real <c>library.media</c> row with a bare numeric id, stamped <c>SegmentKind</c> null exactly
    /// like music. Implementations MUST therefore apply the gh-#99 safe-scope exclusion themselves
    /// (never counting or crediting a safe-loop row as a music airing), since only an async membership
    /// read against <c>library.media.library_id</c> can tell the two apart.
    /// </summary>
    Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct);

    /// <summary>
    /// The rotation ledger's own epoch (SPEC F149.3) — the instant the one-shot migration seed
    /// stamped <c>Gardener:RotationSince</c>, or <see langword="null"/> only if that migration has
    /// never run against this station (a pre-Gardener install).
    /// </summary>
    Task<DateTimeOffset?> GetRotationSinceAsync(CancellationToken ct);

    /// <summary>
    /// The F149.5 "never aired since the ledger began" figure (SPEC F149.3/F149.5, STORY-367 AC7):
    /// PLAYABLE <c>library.media</c> rows (the same predicate <c>MediaRepository.PlayablePredicate</c>
    /// applies — ready, measurable, eligible, not <c>never_play</c>) carrying no
    /// <c>library.media_rotation</c> row at all, or whose <c>play_count</c> is still 0 — i.e. "playable
    /// rows with no ledger row or play_count 0". Excludes gh-#99 safe-scope rows the same way
    /// <see cref="RecordAiringAsync"/> does: a safe-loop row is functional audio, never a candidate the
    /// Gardener should ever count as "waiting to air".
    /// </summary>
    Task<long> GetNeverAiredCountAsync(CancellationToken ct);
}
