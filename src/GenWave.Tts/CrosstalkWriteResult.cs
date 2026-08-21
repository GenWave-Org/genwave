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
    /// <param name="Cause">
    /// SPEC F139.1 (STORY-353, PLAN T330): the F139 cause this discard stamps into
    /// <see cref="LlmCallRing"/>, decided once at the SOURCE that already knows why — each
    /// <see cref="CrosstalkScriptParser.Parse"/> reject branch names its own, and
    /// <see cref="CrosstalkScriptWriter"/>'s own <c>finish_reason: length</c>/exception-catch discards
    /// carry theirs — never re-derived downstream from <see cref="Reason"/>'s text. See
    /// <see cref="LlmCallCause"/>'s own remarks for the full resolution-point map.
    /// </param>
    /// <param name="GenerationAttempted">
    /// SPEC F140 review finding F3 (STORY-354, PLAN T328): <see langword="false"/> ONLY when this
    /// discard happened WITHOUT ever attempting a generation — <c>Llm:Endpoint</c> unset, or a
    /// connect-level transport fault (an <see cref="System.Net.Http.HttpRequestException"/> with no
    /// <see cref="System.Net.Http.HttpRequestException.StatusCode"/>, i.e. no response was ever
    /// received) — both resolve in milliseconds, never a genuine sample of how long generation takes.
    /// Defaults to <see langword="true"/> so every OTHER discard (a truncated completion, a line
    /// failing hygiene/budget, an over-target duration estimate — all AFTER a real round trip) keeps
    /// its existing shape with no call-site change. A caller pacing itself off how long generation
    /// takes (<c>GenWave.Host.Crosstalk.CrosstalkStockPacing</c>) reads this to decide whether an
    /// elapsed time is worth blending into its rolling estimate at all.
    /// </param>
    public sealed record Discarded(string Reason, LlmCallCause Cause, bool GenerationAttempted = true) : CrosstalkWriteResult;
}
