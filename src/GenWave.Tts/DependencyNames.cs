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
}
