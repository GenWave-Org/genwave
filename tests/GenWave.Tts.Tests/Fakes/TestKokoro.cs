namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>Builds the production Kokoro path (<see cref="ActiveEngineSynthesizer"/> over a registry seeded from the spec's options) on a spec's own <see cref="HttpClient"/>.</summary>
public static class TestKokoro
{
    public static ActiveEngineSynthesizer Synthesizer(
        HttpClient http, IOptionsMonitor<TtsOptions> options, PronunciationRuleHitReporter? ruleHits = null) =>
        new(new ActiveVoiceEngine(SeededVoiceEngineRegistry.Over(options), options), new SpeechEngineClient(http, options, new EngineRenderActivity(), ruleHits));

    public static KokoroVoiceLister Lister(HttpClient http, IOptionsMonitor<TtsOptions> options) =>
        new(new SpeechEngineClient(http, options, new EngineRenderActivity()), SeededVoiceEngineRegistry.Over(options));
}
