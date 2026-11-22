namespace GenWave.Tts;

/// <summary>
/// Canonical dependency-name keys for <see cref="IDependencyHealth"/> verdicts (SPEC F70.2,
/// STORY-187) — shared between the probes that write them (<see cref="OllamaHealthProbe"/>,
/// <see cref="KokoroHealthProbe"/>, <see cref="PiperHealthProbe"/>) and any reader (T32's
/// degradation controller, <see cref="FallbackTtsSynthesizer"/>'s render-time decision) so both
/// sides of the store never drift on the string.
/// </summary>
public static class DependencyNames
{
    public const string Ollama = "ollama";
    public const string Kokoro = "kokoro";
    public const string Piper = "piper";
    public const string LlmContextWindow = "llm-context-window";

    /// <summary>
    /// The key of one voice engine's warm-up verdict (SPEC F219.25): written only by <see cref="VoiceEngineWarmUp"/>
    /// and read only by <see cref="VoiceHealthReader"/>. Deliberately not the engine's profile name, which the cheap
    /// kokoro/piper probes already write: a probe that merely reaches a slow engine must not turn a failed warm-up green.
    /// </summary>
    public static string VoiceEngine(string engineName) => $"voice-engine:{engineName}";
}
