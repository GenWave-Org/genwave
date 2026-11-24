using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A recording rule that always throws.</summary>
public sealed class ThrowingRecorderFixtureRule : IRecordingTalkRule
{
    public string Id => "fixture-recorder-throws";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture throwing recorder",
        "Always throws when recording.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct) =>
        throw new InvalidOperationException("fixture recorder failure");
}
