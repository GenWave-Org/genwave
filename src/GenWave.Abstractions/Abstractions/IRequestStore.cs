namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// Write/read seam for <c>station.request</c> (SPEC F87, STORY-224, PLAN T86). Started narrow: T86
/// shipped only what T87's intake endpoint needed — insert (with the wish-retention sweep) plus the
/// station-wide pending-cap eviction path. T88 (STORY-225) adds exactly what the wish parser needs —
/// <see cref="GetForParseAsync"/>, <see cref="ListUnparsedPendingIdsAsync"/>,
/// <see cref="MarkParsedAsync"/> — and nothing a later rung (T89 matcher, T90 fulfillment) would
/// need, which still land their own members in their own tasks rather than speculatively arriving
/// here now.
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
}
