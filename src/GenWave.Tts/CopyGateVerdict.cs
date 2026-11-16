using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// One candidate's result against <see cref="LlmCopyWriter"/>'s gate ladder (SPEC F138.4, F218.12):
/// the truth gate runs first, and only copy that clears it is checked for repeats, so the two lists are
/// never both populated.
/// </summary>
/// <param name="Violations">Truth-gate violations; empty when the truth gate passed.</param>
/// <param name="Hits">Repeat hits; empty unless the truth gate passed and the copy repeats.</param>
internal sealed record CopyGateVerdict(IReadOnlyList<ClaimViolation> Violations, IReadOnlyList<RepetitionHit> Hits)
{
    /// <summary>Every hit's phrase (motif hits are their bare word).</summary>
    public IReadOnlyList<string> RepeatedPhrases { get; } = Hits.Select(h => h.Phrase).ToList();

    /// <summary>The words of the motif hits (T724), for the booth row.</summary>
    public IReadOnlyList<string> MotifWords { get; } =
        Hits.Where(h => h.Kind == RepetitionHitKind.Motif).Select(h => h.Phrase).ToList();

    /// <summary>The phrases of every other hit.</summary>
    public IReadOnlyList<string> PhraseHits { get; } =
        Hits.Where(h => h.Kind != RepetitionHitKind.Motif).Select(h => h.Phrase).ToList();

    public bool Passed => Violations.Count == 0 && Hits.Count == 0;

    public bool FailedTruth => Violations.Count > 0;

    /// <summary>The ring cause for a call whose copy this verdict rejected.</summary>
    public LlmCallCause RejectCause => FailedTruth ? LlmCallCause.TruthGateReject : LlmCallCause.Repeat;
}
