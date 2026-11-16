namespace GenWave.Tts;

using System.Globalization;
using System.Text.Json;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Learns the LLM's context window (SPEC F218.21): Ollama's <c>GET {Llm:Endpoint}/api/ps</c>
/// <c>context_length</c> for <c>Llm:Model</c> — loading the model with an empty-prompt
/// <c>POST /api/generate</c> (Ollama's documented load call; deliberately replaces PLAN T699's "1-token load" — lighter, same effect) if it
/// is not resident — else <see cref="LlmOptions.ContextTokens"/>, else unknown.
/// <para>
/// Endpoint convention matches every other Llm caller: <c>Llm:Endpoint</c> is the server root
/// (<c>http://host:11434</c>) and <see cref="EndpointUri"/> appends the path — no <c>/v1</c> to strip.
/// </para>
/// <para>
/// Cached per model load: a resident model refreshes the value every cycle; a model that has since
/// been unloaded keeps its last known window (no reload on every cycle — that would pin it in VRAM
/// forever); a different endpoint or model drops the cache. A declared
/// <see cref="LlmOptions.ContextTokens"/> is only consulted when Ollama gives no answer.
/// </para>
/// </summary>
public sealed class LlmContextWindow(
    OllamaContextClient ollama,
    IOptionsMonitor<LlmOptions> optionsMonitor,
    ILogger<LlmContextWindow> logger) : ILlmContextWindow
{
    /// <param name="OllamaTokens">The last window Ollama itself reported for this endpoint+model (the per-load cache).</param>
    /// <param name="LoadAttempted">A load was already tried for this endpoint+model; never tried again, whatever its outcome.</param>
    sealed record Reading(string Endpoint, string Model, int? Tokens, int? OllamaTokens, bool Enabled, bool LoadAttempted);

    readonly record struct OllamaOutcome(int? Tokens, bool LoadAttempted);

    volatile Reading reading = new("", "", null, null, false, false);

    /// <summary>True when an LLM endpoint is configured (a window, or a reason for its absence, is meaningful).</summary>
    internal bool Enabled => reading.Enabled;

    public int? Tokens => reading.Tokens;

    public MemoryBudget Budget => MemoryBudget.ForContextWindow(reading.Tokens);

    public string? MemoryOffReason
    {
        get
        {
            var current = reading;
            if (!current.Enabled || Budget.MemoryOn)
                return null;
            return current.Tokens is not { } window
                ? "context window unknown, memory off"
                : string.Create(CultureInfo.InvariantCulture, $"context window {window} < {MemoryBudget.MinContextTokens}, memory off");
        }
    }

    /// <summary>
    /// One refresh. Swallows probe failures; the caller's cancellation or the per-probe timeout
    /// (<see cref="OperationCanceledException"/>) still escapes.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        if (string.IsNullOrEmpty(cfg.Endpoint))
        {
            reading = new Reading("", "", null, null, Enabled: false, LoadAttempted: false);
            return;
        }

        var previous = reading;
        var sameTarget = previous.Endpoint == cfg.Endpoint && previous.Model == cfg.Model;
        var outcome = await ReadFromOllamaAsync(cfg, previous, sameTarget, ct);
        var ollamaTokens = outcome.Tokens ?? (sameTarget ? previous.OllamaTokens : null);
        reading = new Reading(cfg.Endpoint, cfg.Model, ollamaTokens ?? cfg.ContextTokens, ollamaTokens, Enabled: true, outcome.LoadAttempted);
    }

    async Task<OllamaOutcome> ReadFromOllamaAsync(LlmOptions cfg, Reading previous, bool sameTarget, CancellationToken ct)
    {
        var loadAttempted = sameTarget && previous.LoadAttempted;
        var reportedBefore = sameTarget && previous.OllamaTokens is not null;
        if (string.IsNullOrEmpty(cfg.Model))
            return new OllamaOutcome(null, loadAttempted);

        try
        {
            var resident = await ollama.ResidentContextAsync(cfg.Endpoint, cfg.Model, ct);
            if (resident is not null || loadAttempted || reportedBefore)
                return new OllamaOutcome(resident, loadAttempted);

            // At most one load per endpoint+model: a reload resets Ollama's keep_alive and would pin the
            // model in VRAM forever. Recorded before the call so a timeout or failure still counts.
            loadAttempted = true;
            reading = new Reading(cfg.Endpoint, cfg.Model, sameTarget ? previous.Tokens : null,
                sameTarget ? previous.OllamaTokens : null, Enabled: true, LoadAttempted: true);
            await ollama.LoadAsync(cfg.Endpoint, cfg.Model, ct);
            return new OllamaOutcome(await ollama.ResidentContextAsync(cfg.Endpoint, cfg.Model, ct), loadAttempted);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogDebug(ex, "LLM context-window probe got no answer from Ollama");
            return new OllamaOutcome(null, loadAttempted);
        }
    }
}
