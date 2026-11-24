namespace GenWave.Core.TalkRules;

/// <summary>Pure: the same snapshot in gives the same verdict out.</summary>
public interface IJudgingTalkRule : ITalkRule
{
    RuleFlag? Judge(TalkSnapshot after);
}
