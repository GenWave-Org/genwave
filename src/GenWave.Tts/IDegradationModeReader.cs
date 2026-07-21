namespace GenWave.Tts;

/// <summary>
/// Narrow read seam over the degradation mode (SPEC F69.5, F73.1) — the ONE capability
/// <see cref="LlmCopyWriter"/> needs from <see cref="DegradationController"/> to stamp "the mode
/// active at call time" onto every <see cref="LlmCallRing"/> record (STORY-196, T41). Kept separate
/// from <see cref="DegradationController"/>'s full Evaluate/transition surface so
/// <see cref="LlmCopyWriter"/> depends on exactly the one cheap, side-effect-free read it actually
/// needs, not the whole state machine's read/write API (Interface Segregation) — and so every
/// existing <see cref="LlmCopyWriter"/> test double only has to supply the one property it cares
/// about rather than standing up a full <see cref="DegradationController"/> (with its own
/// dependency health/options/clock/logger) just to satisfy an unrelated constructor parameter.
///
/// <see cref="DegradationController"/> implements this directly; nothing else needs to.
/// </summary>
public interface IDegradationModeReader
{
    /// <summary>
    /// The currently applied degradation mode, with NO side effects — never derives or applies a
    /// new transition (contrast <see cref="DegradationController.Evaluate"/>). Safe to read from
    /// any thread at any time, including from inside <see cref="LlmCopyWriter.RequestCompletionAsync"/>'s
    /// own single-flight section: this can never itself force a transition, so there is no lock
    /// ordering to worry about between the two classes' independent locks.
    /// </summary>
    DegradationMode CurrentMode { get; }
}
