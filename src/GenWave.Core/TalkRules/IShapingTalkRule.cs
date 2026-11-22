namespace GenWave.Core.TalkRules;

/// <summary>Adds to the prompt; may keep private state.</summary>
public interface IShapingTalkRule : ITalkRule
{
    ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct);
}
