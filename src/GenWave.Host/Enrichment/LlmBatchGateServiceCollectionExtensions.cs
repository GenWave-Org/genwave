namespace GenWave.Host.Enrichment;

using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;

/// <summary>
/// Wires <see cref="ILlmBatchGate"/> for EVERY offline batch LLM pass that must never compete with
/// on-air copywriting for a fenced model (SPEC F85.3, F95.3): the mood-tagger enrichment batch
/// (STORY-216, T72) and the explicit-classification sweep (STORY-251, T113) both evaluate this SAME
/// singleton gate before their own claim query. <see cref="LlmBatchGate"/> depends on
/// <c>IDegradationModeReader</c>/<c>LlmOptions</c>, both already registered by <c>AddGenWaveTts</c> —
/// MUST run after that call in Program.cs.
/// <c>GenWave.MediaLibrary.IMoodTagger</c>/<c>IExplicitClassifier</c> are themselves registered by
/// <c>AddMediaLibrary</c> (each owns its own composition root, mirroring <c>IYearLookup</c>); this
/// extension only adds the one seam that requires bridging across the module boundary
/// <c>GenWave.MediaLibrary</c> must never cross.
/// </summary>
public static class LlmBatchGateServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveLlmBatchGate(this IServiceCollection services) =>
        services.AddSingleton<ILlmBatchGate, LlmBatchGate>();
}
