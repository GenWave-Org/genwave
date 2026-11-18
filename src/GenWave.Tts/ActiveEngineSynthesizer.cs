namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The primary of the render chain (SPEC F219.3): asks <see cref="IActiveVoiceEngine"/> per call and
/// renders through <see cref="SpeechEngineClient"/> at that engine's own endpoint, over Piper's bare
/// text wire when the engine has the piper profile. An active engine missing from the registry is a
/// failed render, so the fallback chain runs and an empty chain drops the talk (music only).
/// </summary>
public sealed class ActiveEngineSynthesizer(IActiveVoiceEngine active, SpeechEngineClient client) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        var engine = active.Current
            ?? throw new InvalidOperationException($"Tts:Engine '{active.Name}' is not a configured voice engine.");
        return engine.Profile == VoiceEngineProfiles.Piper
            ? client.RenderPiperAsync(engine, context, ct)
            : client.RenderAsync(engine, context, ct);
    }
}
