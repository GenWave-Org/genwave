using System.Text.Json;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A shaper that adds a fixed note and a memo naming itself, and counts its calls.</summary>
public abstract class NoteShaperFixtureRule : IShapingTalkRule
{
    private int calls;

    public abstract string Id { get; }

    /// <summary>The note this shaper adds.</summary>
    protected abstract string Note { get; }

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public int Calls => Volatile.Read(ref calls);

    public TalkRuleDescription Describe() => new(
        "Fixture note shaper",
        "Adds a fixed note.",
        "A fixture for the rule book facts.",
        "Any line.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        return ValueTask.FromResult<RuleShaping?>(new RuleShaping(Note, JsonSerializer.SerializeToElement(new { from = Id })));
    }
}
