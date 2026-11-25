using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that takes the switchboard (law R1).</summary>
public abstract class RuleTakingTheSwitchboard : AbstractBadJudge
{
    protected RuleTakingTheSwitchboard(ITalkRuleSwitchboard switchboard)
    {
    }
}
