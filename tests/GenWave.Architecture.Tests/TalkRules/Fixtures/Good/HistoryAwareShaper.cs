using GenWave.Architecture.Tests.TalkRules.Fixtures;
using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Good;

/// <summary>
/// Good fixture: a shaping rule with a collaborator (a store). Legal for a rule that shapes; proves a rule
/// with a new dependency is built by the catalog with no edit there (F220.2).
/// </summary>
public sealed class HistoryAwareShaper(ITalkLineStore store) : IShapingTalkRule
{
    public string Id => "history-aware";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "History aware",
        "Asks the writer to avoid lines it has used before.",
        "Fresh talk keeps listeners from hearing the same thing twice.",
        "The prompt gains the note: use fresh words.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct) =>
        ValueTask.FromResult<RuleShaping?>(store.LinesFor(0).Count == 0 ? new RuleShaping("Use fresh words.", null) : null);
}
