namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>A registry whose one engine, <c>piper</c>, is seeded from the spec's <c>Tts:PiperPrimaryEndpoint</c>, as production seeding does.</summary>
public sealed class MonitorPiperRegistry(IOptionsMonitor<TtsOptions> options) : IVoiceEngineRegistry
{
    public IReadOnlyList<VoiceEngine> All => Piper() is { } piper ? [piper] : [];

    public VoiceEngine? Get(string name) =>
        string.Equals(name, VoiceEngineProfiles.PiperName, StringComparison.OrdinalIgnoreCase) ? Piper() : null;

    VoiceEngine? Piper()
    {
        var tts = options.CurrentValue;
        return string.IsNullOrEmpty(tts.PiperPrimaryEndpoint)
            ? null
            : new VoiceEngine(VoiceEngineProfiles.PiperName, tts.PiperPrimaryEndpoint, VoiceEngineProfiles.Piper, tts.RenderBudgetSeconds);
    }
}
