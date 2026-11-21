using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Tests.Fakes;

/// <summary>For controller constructions that never reach a named-engine preview.</summary>
sealed class NotUsedNamedEngineSynthesizer : INamedEngineSynthesizer
{
    public Task<string> SynthesizeOnAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct) =>
        throw new InvalidOperationException("A named-engine preview was not expected here.");
}
