using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A judge that always throws.</summary>
public sealed class ThrowingFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-throws";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture throws",
        "Always throws when judging.",
        "A fixture for the rule book facts.",
        "Any line.");

    public RuleFlag? Judge(TalkSnapshot after) => throw new InvalidOperationException("fixture failure");
}
