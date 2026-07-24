namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// Write/read seam for <c>station.request</c> (SPEC F87, STORY-224, PLAN T86). Started narrow: T86
/// shipped only what T87's intake endpoint needed — insert (with the wish-retention sweep) plus the
/// station-wide pending-cap eviction path. T88 (STORY-225) added exactly what the wish parser needs —
/// <see cref="GetForParseAsync"/>, <see cref="ListUnparsedPendingIdsAsync"/>, <see cref="MarkParsedAsync"/>.
/// T89 (STORY-226) adds exactly what the catalog matcher needs — <see cref="MarkMatchedAsync"/>,
/// <see cref="MarkUnmatchedAsync"/>. T90 (STORY-227, SPEC F87.6) adds exactly what the fulfillment
/// rung needs — <see cref="GetOldestLiveAsync"/>, <see cref="ExpireStaleAsync"/>,
/// <see cref="TryMarkFulfilledAsync"/>.
///
/// <para>
/// "Unparsed" discriminator (used by both new read members): <c>status = 'pending' AND artist IS
/// NULL AND title IS NULL AND moods IS NULL</c>. This holds because <see cref="MarkParsedAsync"/> is
/// the ONLY writer of those three columns after <see cref="IRequestStore.InsertAsync"/>'s insert
/// (which always leaves them null), and it never leaves all three null while also leaving
/// <c>status</c> at <c>'pending'</c>: an empty-everything parse (SPEC F87.4's "unparseable ⇒ empty
/// predicates") is exactly the case <c>unmatched: true</c> covers, which flips <c>status</c> to
/// <c>'unmatched'</c> in the SAME write — so a row can only be pending-with-all-null-predicates
/// before its first successful <see cref="MarkParsedAsync"/> call, never after.
/// </para>
/// </summary>
public interface IRequestStore
{
    /// <summary>
    /// Inserts one pending wish, stamped <c>received_at = now()</c> and
    /// <c>expires_at = </c><paramref name="expiresAt"/>, returning the new row's id.
    /// <paramref name="wish"/> is the listener's raw text; never voiced, quoted, or echoed
    /// downstream (SPEC F87.7) — this seam only ever stores it, and only briefly.
    ///
    /// <para>
    /// Also runs the insert-time wish-retention sweep (SPEC F87.8) in the SAME
    /// statement/transaction as the insert — the same "eviction runs inside the write's own
    /// transaction, in application code, not a separate job or trigger" discipline
    /// <c>station.booth_log</c>'s own retention sweep already established. Every row whose
    /// <c>received_at</c> is older than the configured retention window has its <c>wish</c> column
    /// nulled; parsed predicates (<c>artist</c>/<c>title</c>/<c>moods</c>) and the row's outcome
    /// (<c>status</c>/<c>matched_media_id</c>/<c>fulfilled_at</c>) are never touched by the sweep and
    /// stay indefinitely.
    /// </para>
    /// </summary>
    Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Counts rows currently <c>status = 'pending'</c> — the station-wide pending cap (SPEC F87.3,
    /// <c>Requests:PendingCap</c>) reads this before every insert to decide whether an eviction is
    /// needed first.
    /// </summary>
    Task<int> CountPendingAsync(CancellationToken ct);

    /// <summary>
    /// Evicts the single oldest pending row (lowest <c>received_at</c>) — the station-wide
    /// pending-cap eviction (SPEC F87.3): a POST that would push the pending count over
    /// <c>Requests:PendingCap</c> evicts the oldest pending request to make room rather than being
    /// rejected. A no-op when no pending row exists.
    /// </summary>
    Task EvictOldestPendingAsync(CancellationToken ct);

    /// <summary>
    /// Returns <paramref name="id"/>'s wish/expiry for parsing, or <see langword="null"/> when the
    /// row is not (or no longer) a legal parse target: gone, already parsed (see the discriminator in
    /// this interface's own remarks), no longer <c>pending</c>, or its wish text already nulled by the
    /// retention sweep (SPEC F87.8) — every one of those is "nothing left for a parser to do,"
    /// deliberately collapsed into one null rather than distinguished, since the wish parser reacts
    /// identically to all of them: skip this id.
    /// </summary>
    Task<UnparsedRequest?> GetForParseAsync(long id, CancellationToken ct);

    /// <summary>
    /// Lists every row still awaiting its first parse (the discriminator in this interface's own
    /// remarks) — the parser's STARTUP RECOVERY sweep (SPEC F87.4, PLAN T88): the in-memory queue
    /// between the intake controller and the parser does not survive a restart, so any row a crash
    /// left behind is otherwise orphaned until it silently expires. Mirrors
    /// <c>EnrichmentService.ListPendingEnrichmentAsync</c>'s own recovery-query role.
    /// </summary>
    Task<IReadOnlyList<long>> ListUnparsedPendingIdsAsync(CancellationToken ct);

    /// <summary>
    /// Writes one wish's parse outcome (SPEC F87.4): <paramref name="artist"/>/<paramref name="title"/>/
    /// <paramref name="moods"/> are stored verbatim (the parser has already done all filtering —
    /// MoodVocabulary membership, trim-to-non-empty passthrough — this seam never re-validates them).
    /// <paramref name="unmatched"/> is <see langword="true"/> exactly when every predicate came back
    /// empty (F87.4's "unparseable ⇒ empty predicates ⇒ status=unmatched") and flips <c>status</c> to
    /// <c>'unmatched'</c> in the same write; otherwise <c>status</c> is left untouched (still
    /// <c>'pending'</c>, ready for T89's matcher). Deliberately carries no <c>wish</c> parameter at
    /// all — this seam cannot resurrect raw text into a row even by accident (SPEC F87.7/F87.8).
    /// </summary>
    Task MarkParsedAsync(
        long id, string? artist, string? title, IReadOnlyList<string> moods, bool unmatched, CancellationToken ct);

    /// <summary>
    /// Records a successful catalog match (SPEC F87.5, STORY-226, PLAN T89): stamps
    /// <paramref name="mediaId"/> into <c>matched_media_id</c>. <c>status</c> is left untouched —
    /// still <c>pending</c>, ready for T90's fulfillment rung to consult (F87.6); a match is not a
    /// fulfillment. Called at most once per row, immediately after <see cref="MarkParsedAsync"/>
    /// leaves it pending with a non-empty artist/title predicate.
    /// </summary>
    Task MarkMatchedAsync(long id, long mediaId, CancellationToken ct);

    /// <summary>
    /// Flips a row to <c>unmatched</c> (SPEC F87.5) when its artist/title predicate found no catalog
    /// row AND it carries no mood predicate either — nothing left to try. A row with a mood predicate
    /// on a match miss stays <c>pending</c> instead (a vibe request, resolved later at pick time via
    /// the F86.8 mood-filter machinery) — this member is never called for that case.
    /// </summary>
    Task MarkUnmatchedAsync(long id, CancellationToken ct);

    /// <summary>
    /// The oldest still-live pending row with something for the fulfillment rung to try (SPEC F87.6,
    /// STORY-227, PLAN T90): <c>status = 'pending'</c>, not yet past <paramref name="now"/>'s
    /// <c>expires_at</c>, and carrying EITHER a T89 catalog match (<c>matched_media_id</c>) OR at
    /// least one parsed mood (a vibe request) — never neither, and never both meaningfully (a matched
    /// row's moods are irrelevant once matched_media_id exists; the fulfillment rung always prefers
    /// the match). Oldest by <c>received_at</c> first, same tie-break as every other "oldest pending"
    /// read in this interface. <see langword="null"/> when nothing qualifies — the ordinary "no live
    /// request to fulfill" outcome, not an error.
    /// </summary>
    Task<FulfillableRequest?> GetOldestLiveAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Flips every <c>pending</c> row whose <c>expires_at</c> has passed <paramref name="now"/> to
    /// <c>expired</c> (SPEC F87.6) and returns how many rows were flipped. Called opportunistically at
    /// the top of every fulfillment attempt (a cheap indexed UPDATE) rather than on a dedicated timer —
    /// requests expire promptly without needing their own background service. The caller (the
    /// fulfillment rung) is responsible for any booth-log narration; this member itself only writes
    /// the status column.
    /// </summary>
    Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// The one-shot compare-and-swap (SPEC F87.6): flips row <paramref name="id"/> from
    /// <c>pending</c> to <c>fulfilled</c> (stamping <c>fulfilled_at = now()</c>) ONLY if it is still
    /// <c>pending</c> at the moment of the write, returning whether the swap actually landed.
    /// <see langword="false"/> means the row was no longer pending by the time this call reached the
    /// database (already fulfilled/expired/unmatched by a concurrent attempt) — the caller must treat
    /// that exactly like "nothing to fulfill this pick," never retry the same id.
    /// </summary>
    Task<bool> TryMarkFulfilledAsync(long id, CancellationToken ct);
}
