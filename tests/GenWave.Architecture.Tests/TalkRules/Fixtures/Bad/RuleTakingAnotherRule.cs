using GenWave.Architecture.Tests.TalkRules.Fixtures.Good;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that takes another rule (law R1).</summary>
public abstract class RuleTakingAnotherRule : AbstractBadJudge
{
    protected RuleTakingAnotherRule(ShoutingJudge other)
    {
    }
}
