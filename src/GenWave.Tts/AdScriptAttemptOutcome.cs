namespace GenWave.Tts;

/// <summary>
/// One <see cref="AdScriptWriter"/> completion attempt's own outcome (SPEC F160.3, PLAN T400 review
/// F6) — internal orchestration detail, never part of the public <see cref="AdScriptWriter.WriteAsync"/>
/// contract (that stays <see cref="AdScriptWriteResult"/>). Exists so
/// <see cref="AdScriptWriter.WriteAsync"/>'s own "should this attempt get the ONE re-ask" decision
/// matches on a proper closed TYPE — <see langword="is"/> <see cref="ValidatorRefused"/> — rather than
/// inferring "this was a validator refusal, not a transport fault" from
/// <see cref="AdScriptWriteResult.Failed.RuleId"/> being non-null, a nullability check a future field
/// addition to <see cref="AdScriptWriteResult.Failed"/> could silently break without the compiler ever
/// flagging it.
/// </summary>
internal abstract record AdScriptAttemptOutcome
{
    AdScriptAttemptOutcome() { }

    /// <summary>The attempt is DONE — a success, or a fault that never gets a re-ask (a transport
    /// failure, a truncated/empty completion). <see cref="Result"/> is what
    /// <see cref="AdScriptWriter.WriteAsync"/> returns as-is when this is the outcome.</summary>
    public sealed record Resolved(AdScriptWriteResult Result) : AdScriptAttemptOutcome;

    /// <summary>The validator refused this draft — the ONE case <see cref="AdScriptWriter.WriteAsync"/>
    /// spends its single SPEC F160.3 re-ask on. <see cref="RuleId"/>/<see cref="Reason"/> are already
    /// bounded (<see cref="AdScriptWriter"/>'s own <c>BoundReason</c>) and are exactly what the re-ask
    /// prompt names; <see cref="Result"/> is the <see cref="AdScriptWriteResult.Failed"/>
    /// <see cref="AdScriptWriter.WriteAsync"/> falls back to if this SAME rule fires again on the
    /// re-ask.</summary>
    public sealed record ValidatorRefused(string RuleId, string Reason, AdScriptWriteResult.Failed Result) : AdScriptAttemptOutcome;
}
