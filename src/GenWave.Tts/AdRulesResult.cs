namespace GenWave.Tts;

/// <summary>How a rules-mode ad ended (SPEC F220.11, F220.14); <see cref="AdScriptWriter"/> turns each case into its result and its ring entry.</summary>
public abstract record AdRulesResult
{
    AdRulesResult() { }

    /// <summary>A call faulted, was cut short or left nothing after clean-up. Already recorded by the writer; no re-ask was made.</summary>
    public sealed record Faulted(AdScriptWriteResult.Failed Result) : AdRulesResult;

    /// <summary>The reply failed the fixed shape checks (format, stage direction): the spot is failed with no re-ask (F220.11).</summary>
    public sealed record ShapeRefused(string Raw, string RuleId, string Reason) : AdRulesResult;

    /// <summary>The re-ask was flagged again: the spot is failed, and <paramref name="Reason"/> names every flagged rule (F220.14).</summary>
    public sealed record StillFlagged(string Raw, string Reason) : AdRulesResult
    {
        /// <summary>
        /// The <see cref="AdScriptWriteResult.Failed.RuleId"/> a still-flagged ad fails with. Non-null on purpose: the worker records a
        /// failed spot only for a non-null rule id, and the named rules are already in the reason. It is not an <c>AdScriptRuleIds</c>
        /// token and never reaches the cause mapping (the cause is <see cref="LlmCallCause.RuleFlagged"/>).
        /// </summary>
        public const string FailedRuleId = "rule-flagged";
    }

    /// <summary>No switched-on rule flagged the cleaned script: it airs as cleaned (F220.12).</summary>
    public sealed record Accepted(string Raw, string Script, AdRulesAccepted Pending) : AdRulesResult;
}
