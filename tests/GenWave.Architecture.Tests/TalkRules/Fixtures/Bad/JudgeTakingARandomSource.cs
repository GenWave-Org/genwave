using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a judge-only rule that takes a random source (law R2).</summary>
public abstract class JudgeTakingARandomSource : AbstractBadJudge
{
    protected JudgeTakingARandomSource(Random random)
    {
    }
}
