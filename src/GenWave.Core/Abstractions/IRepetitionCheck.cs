using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.11, gh-#293) — pure check of a candidate DJ line against the persona's recent
/// utterances and the shift's spent once-a-shift quirks. No I/O, no clock.
/// </summary>
public interface IRepetitionCheck
{
    /// <summary>Returns every repeated phrase found in <paramref name="copy"/>; empty hits mean the copy is fresh.</summary>
    RepetitionResult Check(string copy, RepetitionContext context);
}
