namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Piper health probe (SPEC F70.2, STORY-190): <c>GET {Tts:Fallback:Endpoint}/</c> — the upstream
/// <c>piper.http_server</c> wrapper the compose <c>piper</c> service runs exposes exactly one
/// route (<c>/</c>) and no dedicated health path. That route ALWAYS answers 500 when called
/// without a <c>?text=</c> query (verified against the real image, even on a perfectly healthy
/// server — it treats "no text" as a request error), so, unlike <see cref="KokoroHealthProbe"/>,
/// this probe cannot use <c>EnsureSuccessStatusCode()</c> as its signal: a healthy Piper would then
/// always report unhealthy. Any HTTP response at all — 200 or 500 alike — proves the process is up
/// and accepting connections; only a connect failure or timeout (the exception this method lets
/// through, same contract as every other <see cref="IDependencyProbe"/>) means Piper is actually
/// down.
/// <para>
/// An empty <c>Tts:Fallback:Endpoint</c> is the disabled-by-design state (F70.1) — mirrors
/// <see cref="OllamaHealthProbe"/>'s empty-<c>Llm:Endpoint</c> handling: returns false
/// (not-configured) without ever calling out.
/// </para>
/// <para>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroHealthProbe"/>/<see cref="OllamaHealthProbe"/> (SPEC F36.1-F36.2):
/// <c>Tts:Fallback:Endpoint</c> is read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
/// per probe, so a live repoint applies to the very next cycle.
/// </para>
/// </summary>
public sealed class PiperHealthProbe(HttpClient http, IOptionsMonitor<TtsFallbackOptions> optionsMonitor) : IDependencyProbe
{
    public string DependencyName => DependencyNames.Piper;

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        var endpoint = optionsMonitor.CurrentValue.Endpoint;
        if (string.IsNullOrEmpty(endpoint))
            return false;   // disabled by design (F70.1) — not a probe failure

        var requestUri = EndpointUri.Combine(endpoint, "/");
        using var response = await http.GetAsync(requestUri, ct);
        // Deliberately no EnsureSuccessStatusCode() — see class remarks. Reaching this line at all
        // means the connection succeeded and a response came back, which is the whole signal.
        return true;
    }
}
