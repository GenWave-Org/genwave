using System.Collections.Concurrent;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A recording rule that keeps the first line of every accepted snapshot it is told about.</summary>
public sealed class RecorderFixtureRule : IRecordingTalkRule
{
    private readonly ConcurrentQueue<string> recorded = new();

    public string Id => "fixture-recorder";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public IReadOnlyCollection<string> Recorded => recorded;

    public TalkRuleDescription Describe() => new(
        "Fixture recorder",
        "Remembers accepted talk.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
    {
        recorded.Enqueue(accepted.Lines[0].Text);
        return ValueTask.CompletedTask;
    }
}
