namespace GenWave.Tts;

/// <summary>
/// Rides the dependency-health cadence (SPEC F218.21, F70.2) to refresh <see cref="LlmContextWindow"/>.
/// Always healthy (the window is a fact, not a dependency verdict — Ollama's own liveness is
/// <see cref="OllamaHealthProbe"/>'s job); returns false only when the LLM is disabled.
/// </summary>
public sealed class LlmContextWindowProbe(LlmContextWindow window) : IDependencyProbe
{
    public string DependencyName => DependencyNames.LlmContextWindow;

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        await window.RefreshAsync(ct);
        return window.Enabled;
    }
}
