namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>A registry whose one engine, <c>kokoro</c>, follows the spec's live <c>Tts:Endpoint</c>, as the seeded production entry does.</summary>
public sealed class MonitorKokoroRegistry(IOptionsMonitor<TtsOptions> options) : IVoiceEngineRegistry
{
    public IReadOnlyList<VoiceEngine> All => [Kokoro()];

    public VoiceEngine? Get(string name) =>
        string.Equals(name, VoiceEngineProfiles.KokoroName, StringComparison.OrdinalIgnoreCase) ? Kokoro() : null;

    VoiceEngine Kokoro()
    {
        var tts = options.CurrentValue;
        return new VoiceEngine(VoiceEngineProfiles.KokoroName, tts.Endpoint, VoiceEngineProfiles.Kokoro, tts.RenderBudgetSeconds);
    }
}
