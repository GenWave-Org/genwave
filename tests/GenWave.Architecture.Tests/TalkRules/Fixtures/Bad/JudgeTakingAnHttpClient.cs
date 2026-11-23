using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a judge-only rule that takes an HttpClient (law R2).</summary>
public abstract class JudgeTakingAnHttpClient : AbstractBadJudge
{
    protected JudgeTakingAnHttpClient(HttpClient http)
    {
    }
}
