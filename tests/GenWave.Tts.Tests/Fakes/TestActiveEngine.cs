namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>An active-engine lookup over one named engine, as a station with that <c>Tts:Engine</c> reads it.</summary>
public static class TestActiveEngine
{
    public static IActiveVoiceEngine Named(string name) => Named(name, profile: name);

    /// <summary>One engine registered as <c>Tts:Engines:&lt;name&gt;</c> with its own <c>Profile</c>.</summary>
    public static IActiveVoiceEngine Named(string name, string profile)
    {
        var engines = new VoiceEnginesOptions();
        engines.Engines[name] = new VoiceEngineEntry { Endpoint = $"http://{name}:8000", Profile = profile };
        return new ActiveVoiceEngine(
            new VoiceEngineRegistry(Options.Create(engines)),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { Engine = name }));
    }
}
