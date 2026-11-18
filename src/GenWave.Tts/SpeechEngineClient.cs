namespace GenWave.Tts;

using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The one typed-HttpClient egress type for the OpenAI-style speech contract (SPEC F219.4–F219.5):
/// <c>POST {Endpoint}/v1/audio/speech</c> and <c>GET {Endpoint}/v1/audio/voices</c> for ANY
/// <see cref="VoiceEngine"/>. The engine (endpoint + profile) is a per-call argument, so adding an
/// engine is configuration, not a new client. No boot-frozen BaseAddress (F36): the absolute URI is
/// built per call. Piper's bare text/plain wire is a different contract, rendered by <see cref="RenderPiperAsync"/> against the same engine endpoint.
/// </summary>
public sealed class SpeechEngineClient(
    HttpClient http,
    IOptionsMonitor<TtsOptions> optionsMonitor,
    PronunciationRuleHitReporter? ruleHits = null)
{
    /// <summary>The synthesizer + voice lister for one engine, sharing this client's HttpClient.</summary>
    public BoundSpeechEngine For(VoiceEngine engine) => new(this, engine);

    public async Task<string> RenderAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        var body = SpeechRequestBody.Build(
            engine.Profile, context.Text, context.Voice, cfg.Format,
            PronunciationRuleSet.FromContext(context.Rules), cfg.SentencePauseSeconds, context.Pace,
            out var matches);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await http.PostAsync(EndpointUri.Combine(engine.Endpoint, "/v1/audio/speech"), content, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = await TransientAudioWriter.WriteAsync(cfg, bytes, ct);

        // Only after the audio landed: "fired" means "aired" (STORY-253, T142).
        ruleHits?.Report(matches, context.Kind, context.IsAudition);
        return path;
    }

    /// <summary>Renders over Piper's bare text/plain wire at the engine's own endpoint (SPEC F99.4, F219.1).</summary>
    public Task<string> RenderPiperAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct) =>
        PiperWireProtocol.RenderAsync(http, engine.Endpoint, context, optionsMonitor.CurrentValue, ct);

    public async Task<IReadOnlyList<string>> ListVoicesAsync(VoiceEngine engine, CancellationToken ct)
    {
        var response = await http.GetAsync(EndpointUri.Combine(engine.Endpoint, "/v1/audio/voices"), ct);
        response.EnsureSuccessStatusCode();

        // Kokoro and the bench shims both answer { "voices": [...] }; the parser takes either entry shape.
        var payload = await response.Content.ReadFromJsonAsync<KokoroVoicesResponse>(ct);
        return payload?.VoiceIds() ?? [];
    }
}
