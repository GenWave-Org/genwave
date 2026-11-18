namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>Builds the production piper-primary path (<see cref="ActiveEngineSynthesizer"/>; <c>Tts:Engine</c> still its default) on a spec's own <see cref="HttpClient"/>.</summary>
public static class TestPiperPrimary
{
    public static ActiveEngineSynthesizer Synthesizer(HttpClient http, IOptionsMonitor<TtsOptions> options) =>
        new(new ActiveVoiceEngine(new MonitorPiperRegistry(options), options), new SpeechEngineClient(http, options));
}
