namespace GenWave.Host.Enrichment;

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Tts;

/// <summary>
/// <see cref="ILlmBatchGate"/> over the F69 degradation state machine (SPEC F85.3, STORY-216, T72):
/// the Host-side bridge <c>GenWave.MediaLibrary.Enrich.EnrichmentService</c>'s mood-tag backfill
/// consumes without GenWave.MediaLibrary ever referencing GenWave.Tts. Reads two independent,
/// already-existing Tts-side signals rather than one derived state:
/// <list type="bullet">
/// <item><see cref="LlmOptions.Endpoint"/> (fresh via <see cref="IOptionsMonitor{TOptions}"/>) — an
/// empty endpoint is checked directly here, rather than trusted to
/// <see cref="IDegradationModeReader.CurrentMode"/> already reflecting "unconfigured": that reader is
/// a passive field updated only when <see cref="DegradationController.Evaluate"/> has actually run
/// (an on-air render or a status GET) — a process that has served neither yet would otherwise see the
/// field's Normal default and wrongly conclude the LLM is healthy.</item>
/// <item><see cref="IDegradationModeReader.CurrentMode"/> — Soft or Hard both decline, mirroring F69.1's
/// "minimized calls"/"zero calls" ladder: only <see cref="DegradationMode.Normal"/> allows a batch
/// (SPEC F85.3 — batch work defers to render-ahead priority, never competing with on-air copywriting
/// for a fenced model).</item>
/// </list>
/// </summary>
sealed class LlmBatchGate(
    IDegradationModeReader degradationModeReader, IOptionsMonitor<LlmOptions> llmOptions) : ILlmBatchGate
{
    public LlmBatchGateDecision Evaluate()
    {
        if (string.IsNullOrEmpty(llmOptions.CurrentValue.Endpoint))
            return new LlmBatchGateDecision(Allowed: false, "LLM not configured (Llm:Endpoint is empty)");

        var mode = degradationModeReader.CurrentMode;
        return mode == DegradationMode.Normal
            ? new LlmBatchGateDecision(Allowed: true, "LLM healthy (Normal)")
            : new LlmBatchGateDecision(Allowed: false, $"LLM degraded ({mode})");
    }
}
