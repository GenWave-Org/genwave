namespace GenWave.Host.Tests.Support;

using GenWave.Tts;
using Microsoft.Extensions.Options;

/// <summary>
/// The real <see cref="VoiceEngineRegistry"/> over engines seeded from a spec's live <see cref="TtsOptions"/>
/// by the production <see cref="VoiceEngineSeeding"/>, so a mid-spec <c>Tts:Endpoint</c> change reaches the next lookup.
/// </summary>
public static class SeededVoiceEngineRegistry
{
    public static VoiceEngineRegistry Over(IOptionsMonitor<TtsOptions> tts) => new(new SeededMonitor(tts));

    sealed class SeededMonitor(IOptionsMonitor<TtsOptions> tts) : IOptionsMonitor<VoiceEnginesOptions>
    {
        public VoiceEnginesOptions CurrentValue
        {
            get
            {
                var engines = new VoiceEnginesOptions();
                VoiceEngineSeeding.Seed(engines, tts.CurrentValue);
                return engines;
            }
        }

        public VoiceEnginesOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<VoiceEnginesOptions, string?> listener) => null;
    }
}
