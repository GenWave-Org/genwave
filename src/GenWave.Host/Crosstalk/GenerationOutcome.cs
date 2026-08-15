using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Crosstalk;

/// <summary>
/// What <see cref="CrosstalkStockWorker"/>'s own <c>GenerateAndAssembleAsync</c> produced (PLAN T286
/// review F4) — <see cref="Assembled"/> null for every non-air outcome, with
/// <see cref="CancelledByBreakWindow"/> telling <c>TickOnceAsync</c> the ONE thing it needs to know
/// to decide pacing: was this a genuine script/render discard (the accept-rate problem the per-show
/// cooldown exists to pace), or a break window opening mid-flight (blameless, retried off-window the
/// very next tick, and must never count against the show).
/// </summary>
/// <param name="Assembled">
/// The assembled asset AND the accepted script it was mixed from, together (round-2 review F10 — the
/// invariant "Script is non-null exactly when Assembled is" used to live in two independent optional
/// members, set together by convention but never enforced by the type; <c>TickOnceAsync</c>'s own
/// mapping carried a dead <c>outcome.Script is { } script ? ... : null</c> ternary as a result, since
/// the null branch could never actually run). <see langword="null"/> for every non-air outcome.
/// </param>
internal sealed record GenerationOutcome(GenerationOutcome.AssembledExchange? Assembled, bool CancelledByBreakWindow)
{
    /// <summary>
    /// SPEC F127.11 (PLAN T287) — the one success payload: the mixed asset
    /// (<see cref="CrosstalkAssemblyResult.Assembled"/>) paired with the accepted script it was mixed
    /// from, so <c>TickOnceAsync</c> maps this straight onto <c>StockedCrosstalkExchange</c>'s own
    /// <c>Result</c>/<c>Script</c> members with no re-derivation and no possibility of one being set
    /// without the other.
    /// </summary>
    internal sealed record AssembledExchange(CrosstalkAssemblyResult.Assembled Result, CrosstalkAiredScript Script);

    /// <summary>The script writer skipped, or the assembler rejected the render/ceiling — a genuine
    /// discard, never a break-window cancellation.</summary>
    public static readonly GenerationOutcome Discarded = new(Assembled: null, CancelledByBreakWindow: false);

    /// <summary>The watchdog observed a break window open mid-flight and cancelled the attempt.</summary>
    public static readonly GenerationOutcome CancelledByWindow = new(Assembled: null, CancelledByBreakWindow: true);
}
