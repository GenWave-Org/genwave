using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A shaper that always throws <see cref="OperationCanceledException"/>, whoever asked.</summary>
public sealed class CancellingShaperFixtureRule : IShapingTalkRule
{
    public string Id => "fixture-shaper-cancels";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture cancelling shaper",
        "Always throws a cancellation.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct) =>
        throw new OperationCanceledException("fixture cancellation");
}
