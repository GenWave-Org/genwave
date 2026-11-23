using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A judge that covers DJ lines but is off until someone switches it on; always flags.</summary>
public sealed class OffByDefaultFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-off-by-default";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.None;

    public TalkRuleDescription Describe() => new(
        "Fixture off by default",
        "Always flags.",
        "A fixture for the switched-off verdict facts.",
        "Any line.");

    public RuleFlag? Judge(TalkSnapshot after) => new RuleFlag("Always", "Never.", string.Empty);
}
