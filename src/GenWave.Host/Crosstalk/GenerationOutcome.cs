using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Crosstalk;

/// <summary>
/// What <see cref="CrosstalkStockWorker"/>'s own <c>GenerateAndAssembleAsync</c> produced (PLAN T286
/// review F4) — <see cref="Assembled"/> null for every non-air outcome, with
/// <see cref="CancelledByBreakWindow"/> telling <c>TickOnceAsync</c> the ONE thing it needs to know
/// to decide the per-show cooldown: was this a genuine script/render discard (the accept-rate
/// problem <c>cooldownUntil</c> exists to pace), or a break window opening mid-flight (blameless —
/// never counts against THIS show's own cooldown — but SPEC F140.3 backs the worker off GLOBALLY for
/// it regardless, so the next attempt, of any show, waits out that delay rather than firing the very
/// next tick). <see cref="GenerationAttempted"/> answers a SEPARATE question for
/// <c>RecordPacingOutcome</c> (round-2 review finding F3): did a real generation actually run long
/// enough to be a genuine timing sample, or did the script writer refuse in milliseconds before ever
/// reaching the network?
/// </summary>
/// <param name="Assembled">
/// The assembled asset AND the accepted script it was mixed from, together (round-2 review F10 — the
/// invariant "Script is non-null exactly when Assembled is" used to live in two independent optional
/// members, set together by convention but never enforced by the type; <c>TickOnceAsync</c>'s own
/// mapping carried a dead <c>outcome.Script is { } script ? ... : null</c> ternary as a result, since
/// the null branch could never actually run). <see langword="null"/> for every non-air outcome.
/// </param>
/// <param name="GenerationAttempted">
/// Round-2 review finding F3 (production bug): <see langword="false"/> ONLY for
/// <see cref="DiscardedPreFlight"/> — a <see cref="CrosstalkWriteResult.Discarded"/> whose OWN
/// <c>GenerationAttempted</c> already reads false (the endpoint-disabled short-circuit, or a
/// connect-level transport fault that never received a response — see
/// <see cref="CrosstalkScriptWriter"/>'s own remarks). <see langword="true"/> for every other
/// outcome, including an ordinary discard AFTER a real round trip — <c>RecordPacingOutcome</c> reads
/// this to decide whether an elapsed time is a genuine sample worth blending into
/// <see cref="CrosstalkStockPacing"/>'s rolling estimate at all.
/// </param>
internal sealed record GenerationOutcome(
    GenerationOutcome.AssembledExchange? Assembled, bool CancelledByBreakWindow, bool GenerationAttempted = true)
{
    /// <summary>
    /// SPEC F127.11 (PLAN T287) — the one success payload: the mixed asset
    /// (<see cref="CrosstalkAssemblyResult.Assembled"/>) paired with the accepted script it was mixed
    /// from, so <c>TickOnceAsync</c> maps this straight onto <c>StockedCrosstalkExchange</c>'s own
    /// <c>Result</c>/<c>Script</c> members with no re-derivation and no possibility of one being set
    /// without the other.
    /// </summary>
    internal sealed record AssembledExchange(CrosstalkAssemblyResult.Assembled Result, CrosstalkAiredScript Script);

    /// <summary>The script writer skipped AFTER a real attempt, or the assembler rejected the
    /// render/ceiling — a genuine discard, never a break-window cancellation, and a real timing
    /// sample (<see cref="GenerationAttempted"/> stays at its default, <see langword="true"/>).</summary>
    public static readonly GenerationOutcome Discarded = new(Assembled: null, CancelledByBreakWindow: false);

    /// <summary>Round-2 review finding F3: the script writer refused in milliseconds, WITHOUT
    /// attempting a generation at all (<c>Llm:Endpoint</c> unset, or a connect-level transport fault
    /// that never received a response). <see cref="GenerationAttempted"/> false tells
    /// <c>RecordPacingOutcome</c> to leave <see cref="CrosstalkStockPacing"/>'s rolling estimate
    /// untouched — blending a near-zero elapsed time in would erode it toward zero on every tick of
    /// an outage, exactly when the runway gate most needs an honest number.</summary>
    public static readonly GenerationOutcome DiscardedPreFlight =
        new(Assembled: null, CancelledByBreakWindow: false, GenerationAttempted: false);

    /// <summary>The watchdog observed a break window open mid-flight and cancelled the attempt — a
    /// real (if truncated) timing sample, never a pre-flight refusal.</summary>
    public static readonly GenerationOutcome CancelledByWindow = new(Assembled: null, CancelledByBreakWindow: true);
}
