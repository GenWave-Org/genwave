namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Piper health probe (SPEC F70.2, STORY-190): <c>OPTIONS {Tts:Fallback:Endpoint}/</c> — the
/// upstream <c>piper.http_server</c> wrapper the compose <c>piper</c> service runs exposes exactly
/// one route (<c>/</c>) and no dedicated health path. A <c>GET</c> (or <c>HEAD</c>) there ALWAYS
/// answers 500 when called without a <c>?text=</c> query AND logs a <c>ValueError</c> traceback
/// server-side on every call — a healthy server probed every 30s emitted a steady 360 error
/// lines/hour into the fleet's log telemetry (gh-#64). <c>OPTIONS</c> is answered by Flask itself
/// without ever invoking the route handler: 200, zero server-side log lines (both verified against
/// the pinned artibex/piper-http digest, 2026-07-21). Either way, unlike
/// <see cref="KokoroHealthProbe"/>, the status code is not the signal: any HTTP response at all
/// proves the process is up and accepting connections; only a connect failure or timeout (the
/// exception this method lets through, same contract as every other
/// <see cref="IDependencyProbe"/>) means Piper is actually down.
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
        // OPTIONS, not GET: Flask answers it without running the route handler, so the probe stays
        // out of piper's error log (gh-#64) — see class remarks.
        using var request = new HttpRequestMessage(HttpMethod.Options, requestUri);
        using var response = await http.SendAsync(request, ct);
        // Deliberately no EnsureSuccessStatusCode() — see class remarks. Reaching this line at all
        // means the connection succeeded and a response came back, which is the whole signal.
        return true;
    }
}
