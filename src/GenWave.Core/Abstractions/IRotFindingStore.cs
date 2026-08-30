using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The Library Gardener's own findings-queue seam (SPEC F153.1, F153.2, F153.9; STORY-374,
/// STORY-375; PLAN T372, gh-#529) — the same "Core-level port a MediaLibrary repository implements
/// directly" placement <see cref="IMediaRotationSink"/>/<see cref="IThumbStore"/> already establish:
/// no third-party type appears anywhere in this signature, so it belongs here rather than in
/// <c>GenWave.MediaLibrary</c>, even though <c>Garden.RotFindingRepository</c> (its one
/// implementation) is the only thing that ever calls Npgsql/Dapper to satisfy it.
///
/// <para>
/// <b>Reconcile methods are per-kind, not one generic call</b> (F153.2's "as built at T372"
/// amendment): each kind's own predicate and evidence shape are different SQL, so each gets its own
/// method here — <see cref="ReconcileDeadFilesAsync"/> is the first (T372); later kinds
/// (near-duplicate, stale-metadata, shelf-dust, unreachable) add their own siblings rather than
/// widening one signature to a lowest-common-denominator shape. Every reconcile runs SET-BASED,
/// inside the repository, in one transaction — a two-statement open/re-open then resolve, never a
/// per-row loop in C#.
/// </para>
/// </summary>
public interface IRotFindingStore
{
    /// <summary>
    /// Reconciles every <see cref="RotKind.DeadFile"/> finding against <c>library.media</c>'s
    /// current state (SPEC F153.3): opens (or re-opens a resolved) finding for a row whose
    /// <c>state = 'failed'</c>, or whose <c>state = 'unavailable'</c> and has stayed that way past
    /// <paramref name="unavailableGrace"/>; resolves an open finding for a row that no longer
    /// matches. A <see cref="RotState.Dismissed"/> row is never touched by either half.
    /// </summary>
    /// <param name="unavailableGrace">How long a row must have been <c>unavailable</c> before it
    /// counts as dead — the caller (<c>Garden.DeadFileGardenerPass</c>) computes this from
    /// <c>Library:Scan:MissThreshold × Library:ScanIntervalSeconds</c>, read live.</param>
    Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct);

    /// <summary>
    /// Dismisses a finding at the store level (STORY-374 AC4): an <see cref="RotState.Open"/> row
    /// moves to <see cref="RotState.Dismissed"/> with <c>dismissed_at</c> stamped, and returns
    /// <see langword="true"/>. Anything else — an unknown id, or a row that is already
    /// <see cref="RotState.Dismissed"/> or currently <see cref="RotState.Resolved"/> — is a no-op
    /// that returns <see langword="false"/>; a dismissed row is then never re-opened by any pass
    /// (SPEC F153.2).
    /// </summary>
    Task<bool> DismissAsync(long findingId, CancellationToken ct);

    /// <summary>
    /// Every finding matching the given filters, newest-opened first, bounded to
    /// <paramref name="limit"/> rows starting at <paramref name="offset"/> (T372 review LOW-2: the
    /// table carries findings forever with no retention, so an unbounded read was a live footgun the
    /// moment the queue had any real depth — T377's admin surface reuses this same paging rather than
    /// adding its own). <paramref name="kind"/>/<paramref name="state"/> <see langword="null"/> means
    /// "any". Default <paramref name="limit"/> 200 matches this codebase's other admin-list page
    /// sizes; callers needing a different page pass it explicitly.
    /// </summary>
    Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0);

    /// <summary>
    /// How many <see cref="RotState.Open"/> findings exist per <see cref="RotKind"/> right now — the
    /// <c>GET /api/status</c> Gardener tile's own aggregate (SPEC F153.9, T377). A kind with zero
    /// open findings is simply absent from the result rather than present with a zero count.
    /// </summary>
    Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct);
}
