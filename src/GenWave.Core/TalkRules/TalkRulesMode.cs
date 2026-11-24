namespace GenWave.Core.TalkRules;

/// <summary>Who decides what a written talk is: today's code, today's code watched by the rule book, or the rule book (F220.18).</summary>
public enum TalkRulesMode
{
    /// <summary>Today's code decides; the rule book is not called.</summary>
    Legacy,

    /// <summary>Today's code decides and airs; the rule book judges attempt 1 silently and the check log keeps both verdicts.</summary>
    Shadow,

    /// <summary>The rule book decides. Accepted as a setting value ahead of its flow (PLAN T800); until then it airs exactly as <see cref="Legacy"/>.</summary>
    Rules,
}
