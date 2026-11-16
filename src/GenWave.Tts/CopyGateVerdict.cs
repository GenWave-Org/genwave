namespace GenWave.Tts;

/// <summary>
/// One candidate's result against <see cref="LlmCopyWriter"/>'s gate ladder (SPEC F138.4, F218.12):
/// the truth gate runs first, and only copy that clears it is checked for repeats, so the two lists are
/// never both populated.
/// </summary>
/// <param name="Violations">Truth-gate violations; empty when the truth gate passed.</param>
/// <param name="RepeatedPhrases">Repeated phrases; empty unless the truth gate passed and the copy repeats.</param>
internal sealed record CopyGateVerdict(IReadOnlyList<ClaimViolation> Violations, IReadOnlyList<string> RepeatedPhrases)
{
    public bool Passed => Violations.Count == 0 && RepeatedPhrases.Count == 0;

    public bool FailedTruth => Violations.Count > 0;

    /// <summary>The ring cause for a call whose copy this verdict rejected.</summary>
    public LlmCallCause RejectCause => FailedTruth ? LlmCallCause.TruthGateReject : LlmCallCause.Repeat;
}
