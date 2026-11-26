using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A shaper that always throws.</summary>
public sealed class ThrowingShaperFixtureRule : IShapingTalkRule
{
    public string Id => "fixture-shaper-throws";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture throwing shaper",
        "Always throws when shaping.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct) =>
        throw new InvalidOperationException("fixture shaper failure");
}
