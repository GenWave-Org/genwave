namespace GenWave.Core.TalkRules;

/// <summary>What <see cref="ITalkRuleSwitchboard.SwitchAsync"/> did.</summary>
public enum TalkRuleSwitchResult
{
    /// <summary>The switch was stored.</summary>
    Stored,

    /// <summary>No rule has that id; nothing stored.</summary>
    UnknownRule,

    /// <summary>The rule cannot cover that kind (or it is not one single kind); nothing stored, on or off.</summary>
    KindNotCovered,
}
