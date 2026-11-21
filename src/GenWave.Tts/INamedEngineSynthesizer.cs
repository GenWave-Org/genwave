using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// Renders one line on one NAMED registry engine (SPEC F219.10), through the same normalization hand-off air uses
/// but with no fallback hop: the admin "Hear it" on an engine that is not the active one.
/// </summary>
public interface INamedEngineSynthesizer
{
    /// <summary>The path of the rendered audio file; throws when that engine cannot render (the caller decides what that means).</summary>
    Task<string> SynthesizeOnAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct);
}
