namespace GenWave.Tts;

/// <summary>
/// One hop in the ordered TTS fallback resiliency chain (gh-#147) — the operator-facing profile
/// shape bound from <c>Tts:Fallback:Profiles:N:*</c>. See <see cref="TtsFallbackOptions"/> for the
/// section layout and legacy flat-key back-compat, <see cref="TtsFallbackChain"/> for how the
/// effective chain is resolved, and <see cref="FallbackTtsSynthesizer"/> for execution order.
/// </summary>
public sealed class TtsFallbackProfile
{
    /// <summary>
    /// Engine kind for this hop — one of <see cref="DependencyNames.Kokoro"/> (<c>"kokoro"</c>:
    /// any kokoro-fastapi-shaped server speaking <c>POST /v1/audio/speech</c>, including a remote
    /// bring-your-own deployment — F70.1's "remote" role) or <see cref="DependencyNames.Piper"/>
    /// (<c>"piper"</c>: the upstream <c>piper.http_server</c> HTTP wrapper the compose
    /// <c>piper</c> service runs). Any other value fails startup validation loudly
    /// (<see cref="TtsFallbackOptionsValidator"/>) — an unknown engine kind is a deployment
    /// mistake to surface at boot, never a hop silently skipped on air.
    /// </summary>
    public string Engine { get; set; } = "";

    /// <summary>This hop's engine base URL. Must be an absolute http/https URL.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Voice semantics are PER ENGINE KIND — the gh-#147 honest-labeling contract, pinned here at
    /// the schema level:
    /// <list type="bullet">
    /// <item><c>"kokoro"</c> — a REAL knob, honored on the wire
    /// (<see cref="KokoroFallbackRenderer"/>): a non-empty value overrides the caller's
    /// per-request voice for this hop; empty renders with the caller's voice unchanged.</item>
    /// <item><c>"piper"</c> — DISPLAY-ONLY: the upstream <c>piper.http_server</c> wrapper bakes
    /// exactly one voice model into the running container (compose's
    /// <c>MODEL_DOWNLOAD_LINK</c>) and exposes no per-request selector, so this value is never
    /// sent on the wire (<see cref="PiperTtsSynthesizer"/> does not read it). It exists so an
    /// operator or UI can see which sidecar voice this hop is EXPECTED to speak with — keep it
    /// matching what compose actually deployed.</item>
    /// </list>
    /// </summary>
    public string Voice { get; set; } = "";

    /// <summary>
    /// Per-hop probe gate: when true, a cached-unhealthy <see cref="IDependencyHealth"/> verdict
    /// for this hop's engine (<see cref="Engine"/> doubles as the dependency name, F70.2) skips
    /// the hop with a WARN instead of attempting it. Default false — the hop is always attempted
    /// when reached, exactly the pre-gh-#147 single-hop behavior: a possibly-stale unhealthy
    /// verdict never blocks a last line of defense.
    /// </summary>
    public bool SkipWhenUnhealthy { get; set; }

    /// <summary>
    /// Optional per-hop render budget in seconds — a hop exceeding it counts as an ordinary hop
    /// failure (<see cref="TimeoutException"/>) and the chain moves on. Null (default) means no
    /// per-hop budget, exactly the pre-gh-#147 behavior: the engine client's own HTTP timeout is
    /// the only limit.
    /// </summary>
    public double? TimeoutSeconds { get; set; }
}
