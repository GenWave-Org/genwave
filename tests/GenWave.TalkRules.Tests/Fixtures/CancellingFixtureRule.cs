using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A broken judge that throws OperationCanceledException from its synchronous Judge.</summary>
public sealed class CancellingFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-cancels";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture cancels",
        "Always throws a cancellation when judging.",
        "A fixture for the rule book facts.",
        "Any line.");

    public RuleFlag? Judge(TalkSnapshot after) => throw new OperationCanceledException();
}
