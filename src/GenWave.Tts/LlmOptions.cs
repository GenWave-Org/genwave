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
    /// How long a persona preview waits for the single-flight LLM gate before declining with
    /// <see cref="GenWave.Core.Domain.PersonaPreviewResult.Busy"/> (503 to the operator). Only the
    /// interactive preview path is bounded — on-air renders keep the original unbounded wait
    /// (F69.6), since their queueing is render-ahead time, not a human staring at a spinner.
    /// <c>0</c> means decline immediately unless the gate is free.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PreviewQueueWaitSeconds { get; set; } = 5;

    /// <summary>
    /// Optional bearer token for the endpoint. Env-only per the F19.3 secrets rule — never stored in
    /// or returned by the settings API.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Live pin for the LLM degradation mode (SPEC F69.3, STORY-188): <c>"auto"</c> (default)
    /// leaves the mode fully automatic; <c>"normal"</c>/<c>"soft"</c>/<c>"hard"</c> (case-insensitive)
    /// holds that mode and suspends auto drop/raise until this is reset back to <c>"auto"</c>.
    /// Allowlisted and validated in <c>GenWave.Host</c> (<c>StationSettingsAllowlist</c>,
    /// <c>SettingValidator</c>); read fresh by <see cref="DegradationController"/> on every
    /// evaluation via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>, so a
    /// live <c>PUT /api/settings</c> applies with no api restart.
    /// </summary>
    public string DegradationPin { get; set; } = "auto";

    /// <summary>
    /// Ring size for the LLM call inspector (SPEC F73.1, STORY-196, T41): <see cref="LlmCallRing"/>
    /// keeps this many of the most recent completed LLM calls in memory, newest first — never
    /// persisted (F73.3). Read fresh via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
    /// on every <see cref="LlmCallRing.Record"/> call, mirroring every other live-adjustable leaf on
    /// this options class.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int CallRingCapacity { get; set; } = 50;
}
