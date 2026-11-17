using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.40, F218.41, F218.44, F218.45, STORY-549/550) — decides whether a break carries a callback and
/// keeps the ask record. <see cref="PlanAsync"/> writes the offer row before the render; <see cref="SettleAsync"/>
/// stamps it once the line is recorded. The memory-on switch (F218.21) is the caller's check.
/// </summary>
public interface ICallbackPlanner
{
    /// <summary>
    /// Null when the break is not a callback break (wrong segment kind, setting 0, a gate or the draw fails, or no
    /// eligible memory); otherwise the offer is written (used = false) and the ask returned.
    /// </summary>
    Task<CallbackAsk?> PlanAsync(CallbackRequest request, CancellationToken ct);

    /// <summary>
    /// Runs the word rule on the aired line and settles the offer. A fallback line, or a missing line or utterance id,
    /// settles used = false. Store failures propagate; the caller decides how to log them (the offer then stays unused).
    /// </summary>
    Task SettleAsync(CallbackAsk ask, long? utteranceId, string? line, bool fallback, CancellationToken ct);
}
