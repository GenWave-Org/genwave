using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule whose text leaves out its own number (law R6).</summary>
public sealed class HiddenNumberJudge : IJudgingTalkRule
{
    public const int MaxWords = 55;

    public string Id => "hidden-number";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new("Hidden number", "Flags a line that is too long.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}
