namespace GenWave.Tts;

/// <summary>
/// Outcome of <see cref="AdScriptWriter.WriteAsync"/> (SPEC F160.1, F160.3, STORY-390 AC2/AC3) —
/// mirrors <c>CrosstalkWriteResult</c>'s own closed-hierarchy shape: a success always carries real,
/// validated script text; a failure always carries a reason, never both, never neither. There is no
/// third "partial" case and no template/salvage rung by design (F160.1's own "skip-only, no template
/// floor" ruling): every failure — a transport fault, a truncated completion, an empty reply, or a
/// validator refusal that survived the ONE re-ask (SPEC F160.3's ladder) — collapses to
/// <see cref="Failed"/>, and the caller (T402's own <c>AdSpotWorker</c>) simply tries again on its own
/// cadence rather than distinguishing WHY this attempt produced nothing.
/// </summary>
public abstract record AdScriptWriteResult
{
    AdScriptWriteResult() { }

    /// <summary>A generated spot script that cleared the validator, raw text exactly as the completion
    /// produced it after standing copy hygiene (SPEC F34.5) — never the parsed
    /// <c>AdScript</c>/<c>AdScriptLine</c> shape, which lives in <c>GenWave.Ads</c> (L10, see
    /// <see cref="AdScriptValidationOutcome"/>'s own remarks for why a caller needing the parsed lines
    /// re-validates this text once more).</summary>
    public sealed record Success(string Script) : AdScriptWriteResult;

    /// <summary>
    /// No spot was produced.
    /// </summary>
    /// <param name="Reason">The one human-readable explanation — logged at Information (SPEC F160.1:
    /// a failed generation is discipline, not an outage, the same posture <c>CrosstalkScriptWriter</c>
    /// takes) and recorded as <see cref="LlmCallRecord.StatusDetail"/>.</param>
    /// <param name="RuleId">The violated <c>AdScriptRuleIds</c> token — <see langword="null"/> for a
    /// transport/generation fault (disabled endpoint, timeout, a truncated or empty completion at
    /// either attempt: SPEC F160.1's own skip-only floor, never re-asked), non-null ONLY when the
    /// SECOND validator refusal (the ONE re-ask already spent) is what failed the spot — the value a
    /// caller stores verbatim as <c>ad_spot.fail_reason</c> (SPEC F160.3).</param>
    /// <param name="Cause">SPEC F139.1's cause taxonomy this failure stamps into <see cref="LlmCallRing"/>
    /// under <see cref="LlmCallKind.AdScript"/> — decided once at the source that already knows why,
    /// never re-derived downstream from <paramref name="Reason"/>'s text.</param>
    public sealed record Failed(string Reason, string? RuleId, LlmCallCause Cause) : AdScriptWriteResult;
}
