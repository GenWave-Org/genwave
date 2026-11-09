namespace GenWave.Core.Domain;

/// <summary>One repeated phrase found by <see cref="Abstractions.IRepetitionCheck"/>.</summary>
/// <param name="Phrase">
/// For a repeat of a recent utterance, the normalised text of the shared run (lowercased, punctuation
/// stripped). For a spent quirk, the rule's key phrase as written and trimmed (its longest quoted span,
/// else its last four words; an unclosed quote mark stays in the phrase, matching ignores it).
/// </param>
public sealed record RepetitionHit(string Phrase);
