using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The Orchestrator's pull seam onto pending owner announcements (SPEC F144.1, STORY-358). Exists
/// only for the single-feeder pull — the lifecycle transitions that mark an announcement aired or
/// re-arm it belong to their own guardians (<see cref="IAnnouncementLifecycle"/>, PLAN T343), never
/// to this seam.
/// </summary>
public interface IAnnouncementSource
{
    /// <summary>
    /// Atomically claims up to <paramref name="max"/> oldest deliverable announcements — pending,
    /// unexpired — oldest-first, and returns them. Claiming is a STATE TRANSITION, never a peek: a
    /// claimed item must not be handed to a second caller. Implementations must be safe under the
    /// single-feeder pull model (one caller at a time; no concurrent-claim race to guard against
    /// beyond that).
    /// <para>
    /// Returns an empty list when nothing is deliverable, or when vending itself is refused — e.g.
    /// the station is public (SPEC F145.1's vend half). The refusal decision belongs to the
    /// implementation behind this seam; the Orchestrator never reads privacy state to make it.
    /// </para>
    /// <para>
    /// The SPEC F144.1 cap (callers pass 2 today) is the CALLER's choice, not this seam's own — this
    /// method places no ceiling of its own on <paramref name="max"/>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct);
}
