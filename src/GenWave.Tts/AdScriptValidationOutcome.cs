namespace GenWave.Tts;

/// <summary>
/// The minimal contract <see cref="AdScriptWriter"/>'s own validate delegate hands back (SPEC F160.1,
/// F160.3, STORY-390 AC2/AC3) — deliberately NOT the full <c>AdScriptValidationResult</c>/<c>AdScript</c>
/// shape <c>GenWave.Ads</c> owns (this project must never reference that one, L10): a caller that needs
/// the parsed lines re-validates the raw script text <see cref="AdScriptWriter.WriteAsync"/> returns on
/// <see cref="AdScriptWriteResult.Success"/> once more against <c>AdScriptValidator.Validate</c> itself
/// — pure and side-effect-free, so re-running it costs nothing and never drifts from what this writer
/// already checked.
///
/// <para>
/// Mirrors <c>CrosstalkWriteResult</c>'s own closed-hierarchy shape one project over: an accept never
/// carries a reason, a refusal always carries exactly one rule id and reason, never both, never neither.
/// </para>
/// </summary>
public abstract record AdScriptValidationOutcome
{
    AdScriptValidationOutcome() { }

    /// <summary>The script cleared every validator rule.</summary>
    public sealed record Accepted : AdScriptValidationOutcome;

    /// <summary>The script broke one rule — <see cref="RuleId"/> is the stable
    /// <c>AdScriptRuleIds</c> token (a wire contract, GenWave.Ads' own vocabulary), never re-derived
    /// from <see cref="Reason"/>'s free text.</summary>
    /// <param name="RuleId">The violated rule's stable id.</param>
    /// <param name="Reason">A human-readable explanation — named in the SPEC F160.3 re-ask line and,
    /// on a second violation, carried onward as the failed spot's own reason. Any length/content is
    /// accepted here — this delegate contract does NOT require a caller to have already bounded it
    /// (the real <c>GenWave.Ads.AdScriptValidator</c> already does, via its own <c>EchoForReason</c>,
    /// but a test double or a future delegate implementation need not). <see cref="AdScriptWriter"/>'s
    /// own <c>BoundReason</c> (PLAN T400 review F7) defensively truncates to 120 chars and strips
    /// control characters before this text ever reaches a re-ask prompt or the ring's
    /// <c>StatusDetail</c>, regardless of what arrives here.</param>
    public sealed record Refused(string RuleId, string Reason) : AdScriptValidationOutcome;
}
