namespace GenWave.Core.TalkRules;

/// <summary>
/// The reasons the rule book records when a rule itself breaks (SPEC F220.9). Shared so the rules-mode flow can tell a
/// broken shaper's mark from a real flag without referencing the rules project.
/// </summary>
public static class TalkRuleReasons
{
    /// <summary>The reason on the flag of a judge that threw.</summary>
    public const string CouldNotCheck = "Could not check";

    /// <summary>The reason recorded on the verdict of a shaper that threw; it never changes the outcome.</summary>
    public const string CouldNotShape = "Could not shape";

    /// <summary>What a recorder that threw is reported as; it never changes the outcome.</summary>
    public const string CouldNotRecord = "Could not record";
}
