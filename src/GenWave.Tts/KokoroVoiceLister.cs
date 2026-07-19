namespace GenWave.Tts;

using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// Typed-HttpClient proxy for Kokoro's <c>GET /v1/audio/voices</c> (SPEC F29.4). The endpoint comes
/// from the shared <c>Tts:Endpoint</c> option — the same value <see cref="KokoroTtsSynthesizer"/>
/// uses — so there is no separate config key for the voices call.
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.4): <c>Tts:Endpoint</c> is
/// read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an absolute URI is built per
/// call (<see cref="EndpointUri"/>), so a live PUT to <c>Tts:Endpoint</c> applies to the very next
/// <c>GET /api/voices</c> with no api restart.
/// </summary>
public sealed class KokoroVoiceLister(HttpClient http, IOptionsMonitor<TtsOptions> optionsMonitor) : ITtsVoiceLister
{
    public async Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/audio/voices");
        var response = await http.GetAsync(requestUri, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var payload = await response.Content.ReadFromJsonAsync<KokoroVoicesResponse>(ct);
        return payload?.VoiceIds() ?? [];
    }
}
