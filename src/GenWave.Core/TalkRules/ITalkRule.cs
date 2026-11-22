namespace GenWave.Core.TalkRules;

public interface ITalkRule
{
    /// <summary>Stable kebab id, e.g. "repeat-phrases"; the switch key.</summary>
    string Id { get; }

    /// <summary>Kinds the rule knows how to judge or shape.</summary>
    TalkKinds CanCover { get; }

    /// <summary>Subset of <see cref="CanCover"/> used when no switch row exists.</summary>
    TalkKinds OnByDefault { get; }

    TalkRuleDescription Describe();
}
