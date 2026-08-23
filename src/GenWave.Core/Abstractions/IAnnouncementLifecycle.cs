namespace GenWave.Core.Abstractions;

/// <summary>
/// The announcement lifecycle guardians' own write/read seam (SPEC F143.2/.3, F144.5/.6, F145.2;
/// STORY-358/359; PLAN T343) — the SAME "Core-level port a MediaLibrary repository implements
/// directly" placement <see cref="IAnnouncementStore"/>'s own remarks already establish one seam
/// over, narrowed to EXACTLY what T343's three guardians consume: the aired-confirmation sink/drain
/// (<see cref="MarkAiredAsync"/>), the periodic sweep loop (<see cref="ExpireStaleAsync"/>,
/// <see cref="FindClaimedPastGraceAsync"/>, <see cref="ReArmAsync"/>), and the private→public flip
/// drain (<see cref="DeclineAllLiveAsync"/>). Every member below was ALREADY implemented on
/// <c>AnnouncementRepository</c> at T337 (the durable store's own total, never-deletes lifecycle
/// transitions) — this interface's own job is narrower: expose that existing capability through a
/// seam GenWave.Host can depend on without widening <see cref="IAnnouncementStore"/>/
/// <see cref="IAnnouncementSource"/> with lifecycle members neither of THEIR OWN callers (the
/// endpoint, the vend) has any use for.
///
/// <para>
/// <b><see cref="DeclineAllLiveAsync"/>, not an id-list overload.</b> SPEC F145.2's flip sweep
/// declines EVERY currently pending/claimed row, unconditionally — there is no candidate list to
/// hand it (unlike <see cref="FindClaimedPastGraceAsync"/>'s narrower re-arm candidate set), so the
/// honest, race-free primitive is one bulk <c>UPDATE ... WHERE state IN (...)</c>, mirroring
/// <see cref="ExpireStaleAsync"/>'s own "finds its own candidates" shape rather than a separate
/// list-then-loop round trip that would open a TOCTOU window between the two calls.
/// </para>
/// </summary>
public interface IAnnouncementLifecycle
{
    /// <summary>
    /// <c>claimed -&gt; aired</c> (SPEC F143.3) — stamped ONLY on a genuine <c>TrackAired</c>
    /// observation of the announcement's own segment (the gh-#612 lesson: aired is observed, never
    /// assumed). Returns the row's own <c>collapse_count</c> (the booth log's own carry, SPEC
    /// F143.3) when the transition applied, or <see langword="null"/> when no matching
    /// <c>claimed</c> row existed — already aired, re-armed back to pending, expired, or an unknown
    /// id. Never throws for that case; it reports <see langword="null"/>.
    /// </summary>
    Task<int?> MarkAiredAsync(long id, CancellationToken ct);

    /// <summary>
    /// Every currently <c>claimed</c> row whose <c>claimed_at</c> is older than
    /// <paramref name="now"/> minus <paramref name="grace"/> — the re-arm sweep's own candidate read
    /// (SPEC F144.5). Callers that also run <see cref="ExpireStaleAsync"/> in the SAME sweep MUST run
    /// it FIRST: F144.5's "TTL permitting" clause means a claimed row whose TTL has already passed
    /// must expire, never re-arm, and running the expiry sweep first guarantees this method only ever
    /// returns rows with TTL still remaining.
    /// </summary>
    Task<IReadOnlyList<long>> FindClaimedPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// <c>claimed -&gt; pending</c> (SPEC F144.5) for one row — clears <c>claimed_at</c> so the vend
    /// seam can deliver it again. Total: a row not currently <c>claimed</c> (already aired, expired,
    /// or unknown) leaves this a no-op, reporting <see langword="false"/>.
    /// </summary>
    Task<bool> ReArmAsync(long id, CancellationToken ct);

    /// <summary>
    /// <c>pending|claimed -&gt; expired</c> (SPEC F143.2) for every row whose <c>expires_at</c> is
    /// before <paramref name="now"/> — visible, never silent (surfaces in the F146 history read).
    /// Returns the count expired.
    /// </summary>
    Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// <c>pending|claimed -&gt; declined</c> (SPEC F145.2) for EVERY row currently live, stamping
    /// <paramref name="reason"/> — the private→public flip's own "nothing is ever held waiting behind
    /// the toggle" sweep. Returns the count declined (zero is a normal, silent outcome — no row was
    /// live at the moment of the flip).
    /// </summary>
    Task<int> DeclineAllLiveAsync(string reason, CancellationToken ct);
}
