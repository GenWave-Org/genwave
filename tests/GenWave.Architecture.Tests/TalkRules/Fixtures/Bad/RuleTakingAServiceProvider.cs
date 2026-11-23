namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that takes the service provider, so it could look another rule up (law R1).</summary>
public abstract class RuleTakingAServiceProvider : AbstractBadJudge
{
    protected RuleTakingAServiceProvider(IServiceProvider services)
    {
    }
}
