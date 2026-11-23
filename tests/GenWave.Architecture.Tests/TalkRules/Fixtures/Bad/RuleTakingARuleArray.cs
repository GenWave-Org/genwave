using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that takes an array of rules, the way round a plain type check (law R1).</summary>
public abstract class RuleTakingARuleArray : AbstractBadJudge
{
    protected RuleTakingARuleArray(ITalkRule[] others)
    {
    }
}
