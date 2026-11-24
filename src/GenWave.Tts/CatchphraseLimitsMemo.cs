namespace GenWave.Tts;

/// <summary>
/// What "Catchphrase limits" offered this break, left on the snapshot under <see cref="RuleId"/> so the rule can
/// read it back with <c>snapshot.MemoOf(this)</c> and nobody else needs to know (F220.8). In shadow the snapshot
/// source writes it from the legacy quirk selector + tagline ledger result; the rule's own shaper writes it in the
/// rules-mode flow (T796). The judge reads it either way.
/// </summary>
/// <param name="Offered">The quirk texts the selector handed the writer this break.</param>
/// <param name="Allowed">The ledger's taglines that may be said now.</param>
/// <param name="Unavailable">The ledger's taglines that are over quota, cooling down, or spent for the shift.</param>
/// <param name="Spent">The capped quirk rules not handed out this break; saying one is a flag.</param>
public sealed record CatchphraseLimitsMemo(
    IReadOnlyList<string> Offered,
    IReadOnlyList<string> Allowed,
    IReadOnlyList<string> Unavailable,
    IReadOnlyList<string> Spent)
{
    /// <summary>The rule id the memo is keyed by.</summary>
    public const string RuleId = "catchphrase-limits";

    /// <summary>The memo for one break's quirk and tagline result, built one way for the shadow snapshot and the shaper.</summary>
    public static CatchphraseLimitsMemo Of(LegacyCatchphraseResult result) =>
        new(result.Quirks.Lines, result.Allowed, result.Unavailable, result.Spent);
}
