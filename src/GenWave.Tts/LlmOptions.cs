using System.ComponentModel.DataAnnotations;

namespace GenWave.Tts;

/// <summary>
/// Configuration for the OpenAI-compatible completions endpoint <see cref="LlmCopyWriter"/> calls
/// (SPEC F34.2-F34.5, F36.2). An empty <see cref="Endpoint"/> is the disabled state — blurbs stay
/// templated, checked fresh via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// on every render so a live edit applies without a restart.
/// </summary>
public sealed class LlmOptions
{
    public const string Section = "Llm";

    /// <summary>OpenAI-compatible chat-completions base URL. Empty = blurbs disabled (F34.2).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Model name passed to the completions call.</summary>
    public string Model { get; set; } = "";

    /// <summary>Per-completion budget in seconds; a miss falls back to the template (F34.4).</summary>
    [Range(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Copy exceeding this length after cleanup is rejected to the fallback (F34.5).</summary>
    [Range(1, int.MaxValue)]
    public int MaxCopyChars { get; set; } = 450;

    /// <summary>
    /// Optional bearer token for the endpoint. Env-only per the F19.3 secrets rule — never stored in
    /// or returned by the settings API.
    /// </summary>
    public string ApiKey { get; set; } = "";
}
