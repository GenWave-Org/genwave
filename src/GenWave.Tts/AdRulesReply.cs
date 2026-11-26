namespace GenWave.Tts;

/// <summary>
/// One ask of the model, after the writer's own fixed clean-up (SPEC F220.11). <paramref name="Raw"/> is the reply as received
/// (empty when the call faulted first). <paramref name="Cleaned"/> is the line-aware clean-up, with no phone rewrite; null when
/// <paramref name="Fault"/> is set, i.e. the call faulted, the reply was cut short or nothing was left after the clean-up. The
/// writer has already recorded a <paramref name="Fault"/> in the ring.
/// </summary>
public sealed record AdRulesReply(string Raw, string? Cleaned, AdScriptWriteResult.Failed? Fault);
