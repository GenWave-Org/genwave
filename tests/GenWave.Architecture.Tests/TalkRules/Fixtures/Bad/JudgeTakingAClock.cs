using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a judge-only rule that takes a clock (law R2).</summary>
public abstract class JudgeTakingAClock : AbstractBadJudge
{
    protected JudgeTakingAClock(TimeProvider clock)
    {
    }
}
