namespace GenWave.Tts;

/// <summary>
/// Which generation surface produced an <see cref="LlmCallRing"/> entry (SPEC F73.1, F127.11, PLAN
/// T282) — orthogonal to <see cref="LlmCallOutcome"/> (what happened) and to
/// <see cref="DegradationMode"/> (what the ladder was doing at the time). Every call
/// <see cref="LlmCopyWriter"/> itself records — on-air, Soft-cadence, or an operator preview alike —
/// is <see cref="Copy"/>; <see cref="CrosstalkScriptWriter"/>'s own completion calls are
/// <see cref="Crosstalk"/>, so an operator reading <c>/api/llm-calls</c> can tell "why was there no
/// banter" apart from an ordinary blurb miss without re-deriving it from the prompt text (F127.11).
/// </summary>
public enum LlmCallKind
{
    /// <summary>An ordinary segment-copy completion (<see cref="LlmCopyWriter"/>) — LeadIn, BackAnnounce, SignOff, SignOn, ContextSegment, or a persona preview.</summary>
    Copy,

    /// <summary>A two-voice banter script completion (<see cref="CrosstalkScriptWriter"/>, SPEC F127.3).</summary>
    Crosstalk,
}
