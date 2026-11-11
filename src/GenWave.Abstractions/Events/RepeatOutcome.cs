namespace GenWave.Core.Events;

/// <summary>What the writer did about a caught repeat (SPEC F218.38): cut the repeated sentences, asked again, or fell to the template.</summary>
public enum RepeatOutcome
{
    /// <summary>The repeated sentences were dropped and the remainder aired.</summary>
    Trimmed,

    /// <summary>Too little was left; the LLM was asked once more.</summary>
    ReAsked,

    /// <summary>Nothing usable after the re-ask; the template aired.</summary>
    Template,
}
