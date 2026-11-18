namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// One <see cref="VoiceEngine"/> seen through the engine-agnostic contracts: <see cref="ITtsSynthesizer"/>
/// and <see cref="ITtsVoiceLister"/> (SPEC F219.4–F219.5). Holds no HTTP of its own — every call goes
/// through <see cref="SpeechEngineClient"/>, the single egress type.
/// </summary>
public sealed class BoundSpeechEngine(SpeechEngineClient client, VoiceEngine engine) : ITtsSynthesizer, ITtsVoiceLister
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, null), ct);

    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct) =>
        client.RenderAsync(engine, context, ct);

    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        client.ListVoicesAsync(engine, ct);
}
