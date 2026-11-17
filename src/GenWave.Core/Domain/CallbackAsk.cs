namespace GenWave.Core.Domain;

/// <summary>
/// A callback break's ask: the written offer and the one memory the prompt carries (SPEC F218.40, F218.44).
/// Carries the exclusion texts so settling applies the same word rule the eligibility check used.
/// </summary>
public sealed record CallbackAsk(long OfferId, RecalledMemory Memory, IReadOnlyList<string?> ExclusionTexts);
