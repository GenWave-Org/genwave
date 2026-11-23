using GenWave.Architecture.Tests.TalkRules.Fixtures;
using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a judge-only rule that takes a store (law R2).</summary>
public abstract class JudgeTakingAStore : AbstractBadJudge
{
    protected JudgeTakingAStore(ITalkLineStore store)
    {
    }
}
