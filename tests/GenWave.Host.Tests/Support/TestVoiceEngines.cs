namespace GenWave.Host.Tests.Support;

using GenWave.Tts;
using Microsoft.Extensions.Options;

/// <summary>Builds the engine registry a spec hands to code that validates engine names (SPEC F219.1).</summary>
public static class TestVoiceEngines
{
    /// <summary>A registry holding <c>kokoro</c> and <c>piper</c> — the seeded pair on a piper-primary install.</summary>
    public static IVoiceEngineRegistry KokoroAndPiper() => With(
        VoiceEngineProfiles.KokoroName, VoiceEngineProfiles.PiperName);

    /// <summary>A registry holding exactly the named engines, each using the profile of its own name.</summary>
    public static IVoiceEngineRegistry With(params string[] names)
    {
        var options = new VoiceEnginesOptions();
        foreach (var name in names)
            options.Engines[name] = new VoiceEngineEntry { Endpoint = $"http://{name}:8000", Profile = name };
        return new VoiceEngineRegistry(Options.Create(options));
    }

    /// <summary>An active-engine lookup over one named engine, as a station with that <c>Tts:Engine</c> reads it.</summary>
    public static IActiveVoiceEngine Active(string name) => new ActiveVoiceEngine(
        With(name), new FakeOptionsMonitor<TtsOptions>(new TtsOptions { Engine = name }));
}
