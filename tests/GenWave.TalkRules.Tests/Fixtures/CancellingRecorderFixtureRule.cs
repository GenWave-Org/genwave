using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A recording rule that always throws <see cref="OperationCanceledException"/>, whoever asked.</summary>
public sealed class CancellingRecorderFixtureRule : IRecordingTalkRule
{
    public string Id => "fixture-recorder-cancels";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture cancelling recorder",
        "Always throws a cancellation.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct) =>
        throw new OperationCanceledException("fixture cancellation");
}
