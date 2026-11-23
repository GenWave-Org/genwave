using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>
/// Base for bad fixtures whose fault is in the constructor (laws R1, R2). Abstract on purpose: the laws read
/// constructors by reflection, and the production scan (<c>AddTalkRules</c>) skips abstract types, so these
/// unsatisfiable constructors are never registered. The concrete bad fixtures are registered by the R5 scan of this
/// assembly; that is harmless because their constructors can be satisfied.
/// </summary>
public abstract class AbstractBadJudge : IJudgingTalkRule
{
    public string Id => "abstract-bad-judge";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new("Bad.", "Bad.", "Bad.", "Bad.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}
