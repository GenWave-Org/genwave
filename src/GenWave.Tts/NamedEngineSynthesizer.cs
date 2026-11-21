using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// <see cref="INamedEngineSynthesizer"/> over the air's own normalization body
/// (<see cref="NormalizingTtsSynthesizer.SynthesizeViaAsync"/>) and the shared wire seam
/// (<see cref="SpeechEngineClient.RenderOnAsync"/>), so rule suppression, card-correction refresh and fired-correction
/// reporting are the same as on air.
/// </summary>
public sealed class NamedEngineSynthesizer(NormalizingTtsSynthesizer normalizer, SpeechEngineClient client)
    : INamedEngineSynthesizer
{
    public Task<string> SynthesizeOnAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct) =>
        normalizer.SynthesizeViaAsync(context, (c, token) => client.RenderOnAsync(engine, c, token), ct, engine);
}
