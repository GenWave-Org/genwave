namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.17) — one chat completion against the configured LLM, for the life extractor. Lives in Core
/// so <c>GenWave.Memory</c> never sees <c>GenWave.Tts</c>, where the endpoint, key and timeout options are.
/// </summary>
public interface ILifeClaimLlm
{
    /// <summary>
    /// The model's reply text, or null when no LLM is configured. A transport or HTTP fault throws; the extractor
    /// logs it and drops the copy.
    /// </summary>
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
