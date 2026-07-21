namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Kokoro health probe (SPEC F70.2): <c>GET {Tts:Endpoint}/health</c> — kokoro-fastapi's dedicated
/// liveness route (<c>{"status": "healthy"}</c>, present since upstream v0.6.0, the pin in
/// <c>compose.yaml</c>), lighter than round-tripping <c>/v1/audio/voices</c> for a plain up/down
/// check. Unlike <c>Llm:Endpoint</c>, <see cref="TtsOptions.Endpoint"/> has no "disabled by
/// design" state (it is <c>[Required]</c>-validated at startup) — every failure here is a real
/// failure, never a not-configured verdict.
/// <para>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroTtsSynthesizer"/>/<see cref="KokoroVoiceLister"/> (SPEC F36.1–F36.2):
/// <c>Tts:Endpoint</c> is read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> per
/// probe, so a live repoint applies to the very next cycle.
/// </para>
/// </summary>
public sealed class KokoroHealthProbe(HttpClient http, IOptionsMonitor<TtsOptions> optionsMonitor) : IDependencyProbe
{
    public string DependencyName => DependencyNames.Kokoro;

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        var endpoint = optionsMonitor.CurrentValue.Endpoint;
        var requestUri = EndpointUri.Combine(endpoint, "/health");
        var response = await http.GetAsync(requestUri, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx
        return true;
    }
}
