namespace GenWave.Tts;

/// <summary>
/// Turns a per-engine voice map into the voice id for the engine speaking right now (SPEC F219.8/F219.9). The
/// engine is read per call, so a saved <c>Tts:Engine</c> applies to the next render.
/// </summary>
public interface IVoiceResolver
{
    /// <summary>The map's voice for the active engine, or null when it has none (the talk then drops).</summary>
    string? Resolve(VoiceMap voices);
}
