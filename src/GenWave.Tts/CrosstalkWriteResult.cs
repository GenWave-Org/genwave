namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/> (SPEC F127.3, F127.4, STORY-326)
/// — mirrors <see cref="GenWave.Core.Domain.PersonaPreviewResult"/>'s closed-hierarchy shape (an
/// accepted script always carries real, fully-validated lines; a discard always carries a reason,
/// never both, never neither). There is no third "partial" case and no template/salvage rung by
/// design (F127.4): every failure — a transport fault, a malformed reply, a line that fails hygiene
/// or its budget, an over-target duration estimate — collapses to <see cref="Discarded"/>, and the
/// caller (a LATER task's <c>CrosstalkPlanner</c>/stock-timer loop) simply tries again on its own
/// cadence rather than distinguishing WHY this attempt produced nothing.
/// </summary>
public abstract record CrosstalkWriteResult
{
    CrosstalkWriteResult() { }

    /// <summary>A fully validated, ready-to-render two-voice script — the published
    /// <see cref="CrosstalkAiredScript"/> shape directly (round-2 review F8), so a caller carrying it
    /// forward onto <c>GenWave.Orchestration</c>/<c>GenWave.MediaLibrary</c> needs no mapping.</summary>
    public sealed record Accepted(CrosstalkAiredScript Script) : CrosstalkWriteResult;

    /// <summary>
    /// No exchange was produced. <see cref="Reason"/> is the SAME text logged at Information (SPEC
    /// F127.4 — a discard is never a WARN, banter is optional color) and recorded as
    /// <see cref="LlmCallRecord.StatusDetail"/> under <see cref="LlmCallOutcome.Rejected"/> (a content
    /// validation miss) or <see cref="LlmCallOutcome.Failed"/>/<see cref="LlmCallOutcome.Timeout"/> (a
    /// transport miss) — one string, one source of truth for "why was there no banter" across the log
    /// line, the ring, and this return value.
    /// </summary>
    public sealed record Discarded(string Reason) : CrosstalkWriteResult;
}
