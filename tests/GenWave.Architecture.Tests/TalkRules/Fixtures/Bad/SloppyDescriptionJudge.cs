using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule with a snake-case id, unpunctuated text and a default outside what it can cover (law R6).</summary>
public sealed class SloppyDescriptionJudge : IJudgingTalkRule
{
    public string Id => "Sloppy_Rule";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat | TalkKinds.Ad;

    public TalkRuleDescription Describe() => new("sloppy rule", "flags things", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}
