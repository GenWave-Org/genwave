namespace GenWave.Ads;

/// <summary>
/// One rule an <see cref="AdScriptValidator"/> check refused a script on (SPEC F160.3).
/// </summary>
/// <param name="RuleId">One of <see cref="AdScriptRuleIds"/> — the stable, snake_case token stored as
/// <c>fail_reason</c> and surfaced in a save-time 400.</param>
/// <param name="Reason">A human-readable, operator-facing explanation — logged/echoed, never a second
/// source of truth for which rule fired (that is always <paramref name="RuleId"/>).</param>
public sealed record AdScriptViolation(string RuleId, string Reason);
