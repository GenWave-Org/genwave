using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that takes the switches (law R1).</summary>
public abstract class RuleTakingTheSwitches : AbstractBadJudge
{
    protected RuleTakingTheSwitches(ITalkRuleSwitches switches)
    {
    }
}
