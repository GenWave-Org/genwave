namespace GenWave.Tts;

using GenWave.Core.Abstractions;

/// <summary>
/// Kokoro's <c>GET /v1/audio/voices</c> (SPEC F29.4), through <see cref="SpeechEngineClient"/> (PLAN T749)
/// at the registry's <c>kokoro</c> engine, the same entry the render chain uses. The registry is read per
/// call (SPEC F36.1–F36.4), so a live <c>Tts:Endpoint</c> / <c>Tts:Engines:kokoro</c> change applies to the
/// very next <c>GET /api/voices</c> with no api restart. No kokoro engine configured means no voices.
/// </summary>
public sealed class KokoroVoiceLister(SpeechEngineClient client, IVoiceEngineRegistry registry) : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        registry.Get(VoiceEngineProfiles.KokoroName) is { } kokoro
            ? client.ListVoicesAsync(kokoro, ct)
            : Task.FromResult<IReadOnlyList<string>>([]);
}
