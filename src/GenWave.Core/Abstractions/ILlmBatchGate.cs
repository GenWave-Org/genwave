namespace GenWave.Core.Abstractions;

/// <summary>
/// The offline-batch gate every low-priority LLM consumer checks ONCE per batch, never per item
/// (SPEC F85.3) — today's only consumer is the mood-tagger enrichment pass
/// (<c>GenWave.MediaLibrary.Enrich.EnrichmentService</c>'s mood-tag backfill, STORY-216, T72).
///
/// Kept narrow and living here — NOT in <c>GenWave.Tts</c>, where the actual F69 degradation state
/// machine (<c>DegradationController</c>) and <c>Llm:*</c> configuration live — because
/// <c>GenWave.MediaLibrary</c> must never reference <c>GenWave.Tts</c> (see
/// <c>EnrichmentService</c>'s own remarks on the boundary). <c>GenWave.Host</c> implements this
/// interface over <c>IDegradationModeReader</c> and <c>LlmOptions</c> (both Tts-side) and registers
/// it, so the batch consumes the SAME degradation truth the on-air copywriter gates on, without
/// GenWave.MediaLibrary ever seeing the Tts assembly.
/// </summary>
public interface ILlmBatchGate
{
    /// <summary>
    /// Whether an offline batch may attempt the configured LLM right now. Batch work must never
    /// compete with on-air copywriting for a fenced model (SPEC F85.3): a decision is allowed only
    /// while the LLM is fully healthy (F69 Normal mode) AND configured — Soft, Hard, and unconfigured
    /// all decline, with <see cref="LlmBatchGateDecision.Reason"/> carrying the one line a caller
    /// logs for the whole skipped batch.
    /// </summary>
    LlmBatchGateDecision Evaluate();
}
