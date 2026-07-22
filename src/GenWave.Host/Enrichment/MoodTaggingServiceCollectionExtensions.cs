namespace GenWave.Host.Enrichment;

using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;

/// <summary>
/// Wires <see cref="ILlmBatchGate"/> for the mood-tagger enrichment batch (SPEC F85.3, STORY-216,
/// T72). <see cref="LlmBatchGate"/> depends on <c>IDegradationModeReader</c>/<c>LlmOptions</c>,
/// both already registered by <c>AddGenWaveTts</c> — MUST run after that call in Program.cs.
/// <c>GenWave.MediaLibrary.IMoodTagger</c> itself is registered by <c>AddMediaLibrary</c> (it owns
/// its own composition root, mirroring <c>IYearLookup</c>); this extension only adds the one seam
/// that requires bridging across the module boundary <c>GenWave.MediaLibrary</c> must never cross.
/// </summary>
public static class MoodTaggingServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveMoodTaggingGate(this IServiceCollection services) =>
        services.AddSingleton<ILlmBatchGate, LlmBatchGate>();
}
