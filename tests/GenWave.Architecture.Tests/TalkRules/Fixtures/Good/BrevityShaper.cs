using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Good;

/// <summary>Good fixture: a shaping-only rule (the other legal part).</summary>
public sealed class BrevityShaper : IShapingTalkRule
{
    public string Id => "brevity-note";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Brevity note",
        "Asks the writer to keep it short.",
        "Short talk keeps the music moving.",
        "The prompt gains the note: keep it short.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct) =>
        ValueTask.FromResult<RuleShaping?>(new RuleShaping("Keep it short.", null));
}
