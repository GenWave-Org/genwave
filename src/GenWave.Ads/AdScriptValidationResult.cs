namespace GenWave.Ads;

/// <summary>
/// Outcome of <see cref="AdScriptValidator.Validate"/> (SPEC F160.3, STORY-390) — the
/// <c>CrosstalkWriteResult</c> shape: an accepted script always carries the fully parsed lines, a
/// refusal always carries exactly one <see cref="AdScriptViolation"/> (first-rule-wins — never a list),
/// never both, never neither.
/// </summary>
public abstract record AdScriptValidationResult
{
    AdScriptValidationResult() { }

    /// <summary>The script cleared every rule, in order.</summary>
    public sealed record Accepted(AdScript Script) : AdScriptValidationResult;

    /// <summary>The FIRST rule the script broke, in the fixed evaluation order (format, duration,
    /// brand collision, phone shape, audience posture) — never a full list of every rule broken.</summary>
    public sealed record Refused(AdScriptViolation Violation) : AdScriptValidationResult;
}
