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
internal sealed record GenerationOutcome(CrosstalkAssemblyResult.Assembled? Assembled, bool CancelledByBreakWindow)
{
    /// <summary>The script writer skipped, or the assembler rejected the render/ceiling — a genuine
    /// discard, never a break-window cancellation.</summary>
    public static readonly GenerationOutcome Discarded = new(Assembled: null, CancelledByBreakWindow: false);

    /// <summary>The watchdog observed a break window open mid-flight and cancelled the attempt.</summary>
    public static readonly GenerationOutcome CancelledByWindow = new(Assembled: null, CancelledByBreakWindow: true);
}
