namespace GenWave.Core.Domain;

/// <summary>One LLM copy the gates accepted (not a template fallback), handed to the life extractor (SPEC F218.17).</summary>
/// <param name="PersonaId">The persona who spoke it.</param>
/// <param name="PersonaName">That persona's display name, used to phrase a shared outing from each side.</param>
/// <param name="Text">The raw accepted copy, before the TTS normaliser.</param>
public sealed record AcceptedCopy(long PersonaId, string PersonaName, string Text);
