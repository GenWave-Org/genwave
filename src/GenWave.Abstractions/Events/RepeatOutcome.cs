namespace GenWave.Core.Events;

/// <summary>What the writer did about a caught repeat (SPEC F218.38): cut the repeated sentences, asked again, fell to the template, or (crosstalk) dropped the line.</summary>
public enum RepeatOutcome
{
    /// <summary>The repeated sentences were dropped and the remainder aired.</summary>
    Trimmed,

    /// <summary>Too little was left; the LLM was asked once more.</summary>
    ReAsked,

    /// <summary>Nothing usable after the re-ask; the template aired.</summary>
    Template,

    /// <summary>Crosstalk only: the line left the exchange (F218.14); no template airs.</summary>
    Dropped,
}
