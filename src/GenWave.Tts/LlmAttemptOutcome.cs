namespace GenWave.Tts;

/// <summary>Outcome of a single <see cref="LlmCopyWriter"/> completion attempt (SPEC F34.8).</summary>
public enum LlmAttemptOutcome
{
    /// <summary>The completion returned usable copy that passed hygiene.</summary>
    Ok,

    /// <summary>Any miss — timeout, non-2xx, connect failure, empty/over-length copy.</summary>
    Failed,
}
