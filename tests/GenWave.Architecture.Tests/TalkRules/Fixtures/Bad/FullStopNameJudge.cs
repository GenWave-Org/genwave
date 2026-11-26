using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule whose name ends with a full stop (law R6).</summary>
public sealed class FullStopNameJudge : IJudgingTalkRule
{
    public string Id => "full-stop-name";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.None;

    public TalkRuleDescription Describe() => new("Full stop name.", "Flags nothing.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}
