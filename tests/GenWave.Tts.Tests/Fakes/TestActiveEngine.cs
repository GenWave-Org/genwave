namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>An active-engine lookup over one named engine, as a station with that <c>Tts:Engine</c> reads it.</summary>
public static class TestActiveEngine
{
    public static IActiveVoiceEngine Named(string name)
    {
        var engines = new VoiceEnginesOptions();
        engines.Engines[name] = new VoiceEngineEntry { Endpoint = $"http://{name}:8000", Profile = name };
        return new ActiveVoiceEngine(
            new VoiceEngineRegistry(Options.Create(engines)),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { Engine = name }));
    }
}
