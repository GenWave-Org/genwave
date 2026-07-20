namespace GenWave.Tts;

/// <summary>
/// Canonical dependency-name keys for <see cref="IDependencyHealth"/> verdicts (SPEC F70.2,
/// STORY-187) — shared between the probes that write them (<see cref="OllamaHealthProbe"/>,
/// <see cref="KokoroHealthProbe"/>, and T34's future Piper probe) and any reader (T32's
/// degradation controller, T34's fallback decision) so both sides of the store never drift on
/// the string.
/// </summary>
public static class DependencyNames
{
    public const string Ollama = "ollama";
    public const string Kokoro = "kokoro";
}
