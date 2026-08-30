namespace GenWave.Core.Abstractions;

/// <summary>
/// The Library Gardener's push-guard report seam (SPEC F153.4; STORY-375; PLAN T373, gh-#529) —
/// <c>GenWave.Host.Engine.MediaExistencePushGuard</c>'s own fire-and-forget hook after it declines
/// a push whose file is absent from disk: opens (or re-opens) a <c>dead_file</c> finding for the
/// media row, evidence <c>{"reason": "push_missing", "since": &lt;now&gt;}</c>, giving a missing
/// file near-instant visibility in the Gardener's queue instead of waiting for the scan's own
/// state-based reconcile (<c>Garden.DeadFileGardenerPass</c>) to catch up after
/// <c>Library:Scan:MissThreshold</c> ticks.
///
/// <para>
/// <b>The caller MUST treat this as fire-and-forget and MUST NOT let a failure delay a push</b>
/// (F153.4's own contract): the push has already been declined by the time this is called, and a
/// reporter failure must log exactly one WARN naming the reporter and change no push outcome or
/// timing beyond that WARN. This method itself may still throw on a genuine failure (e.g. the
/// database unreachable) — catching that and turning it into the required WARN is the CALLER's
/// job, not this seam's; the one implementation, <c>Garden.DeadFileReporter</c>, calls straight
/// through to <see cref="IRotFindingStore.OpenDeadFileAsync"/> (Dapper-free itself — L2), the only
/// place SQL for this write lives.
/// </para>
/// </summary>
public interface IDeadFileReporter
{
    /// <summary>Reports that <paramref name="mediaId"/>'s file was absent at push time. An unknown
    /// media id is a silent no-op at the store level (no catalog row to attach a finding to) —
    /// never a thrown exception on that path alone.</summary>
    Task ReportMissingAsync(long mediaId, CancellationToken ct);
}
