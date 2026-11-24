using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: its own number 1 appears only inside the decimal 1.5 (law R6).</summary>
public sealed class DecimalHidingJudge : IJudgingTalkRule
{
    public const int Limit = 1;

    public string Id => "decimal-hiding";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new("Decimal hiding.", "Flags a pause of more than 1.5 seconds.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}
