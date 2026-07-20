namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Ollama health probe (SPEC F70.2): <c>GET {Llm:Endpoint}/api/version</c> — the lightest
/// documented Ollama endpoint (returns just the running version string; unlike
/// <c>/api/tags</c> it never enumerates or touches the local model store). An empty
/// <c>Llm:Endpoint</c> is LLM-disabled by design (SPEC F34.2, <see cref="LlmOptions.Endpoint"/>) —
/// this returns false (not-configured) without ever calling out, rather than treating "off" as a
/// failure.
/// <para>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroTtsSynthesizer"/>/<see cref="KokoroVoiceLister"/> (SPEC F36.1–F36.2):
/// <c>Llm:Endpoint</c> is read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> per
/// probe, so a live repoint applies to the very next cycle.
/// </para>
/// </summary>
public sealed class OllamaHealthProbe(HttpClient http, IOptionsMonitor<LlmOptions> optionsMonitor) : IDependencyProbe
{
    public string DependencyName => DependencyNames.Ollama;

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        var endpoint = optionsMonitor.CurrentValue.Endpoint;
        if (string.IsNullOrEmpty(endpoint))
            return false;   // disabled by design (F34.2) — not a probe failure

        var requestUri = EndpointUri.Combine(endpoint, "/api/version");
        var response = await http.GetAsync(requestUri, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx
        return true;
    }
}
