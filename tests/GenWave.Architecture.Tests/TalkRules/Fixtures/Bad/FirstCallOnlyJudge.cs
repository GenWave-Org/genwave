using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a judge that flags only the first snapshot it is ever given, so its verdict depends on when it ran (law R3).</summary>
public sealed class FirstCallOnlyJudge : IJudgingTalkRule
{
    private int calls;

    public string Id => "first-call-only";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => CanCover;

    public TalkRuleDescription Describe() => new("First call only", "Flags the first thing it sees.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) =>
        calls++ == 0 ? new RuleFlag("First", "Write it again.", string.Empty) : null;
}
