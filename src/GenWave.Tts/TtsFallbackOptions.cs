namespace GenWave.Tts;

/// <summary>
/// Configuration for the Piper local-fallback TTS sidecar (SPEC F70.1, STORY-190). An empty
/// <see cref="Endpoint"/> is the disabled state: <see cref="FallbackTtsSynthesizer"/> then routes
/// every render straight to the primary (Kokoro) synthesizer with no health read and no retry —
/// exactly pre-T34 behavior, so a bare deployment with no Piper sidecar is unaffected. The shipped
/// <c>compose.yaml</c> sets both properties for its own <c>piper</c> service, so a real fresh
/// deploy of the full stack has the fallback enabled out of the box.
/// </summary>
public sealed class TtsFallbackOptions
{
    public const string Section = "Tts:Fallback";

    /// <summary>Piper HTTP wrapper base URL. Empty = fallback disabled (F70.1).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// The Piper voice model the fallback sidecar is expected to be running (e.g.
    /// <c>"en_US-lessac-medium"</c>) — operator-facing documentation of what
    /// <c>MODEL_DOWNLOAD_LINK</c> the compose <c>piper</c> service was started with. The upstream
    /// <c>piper.http_server</c> wrapper that service runs bakes exactly one voice model into the
    /// running container and exposes no per-request voice selector, so this value is never sent on
    /// the wire (<see cref="PiperTtsSynthesizer"/> does not read it) and does not hot-swap the
    /// sidecar's model — it only needs to match compose.yaml's `piper` service for the deployed
    /// voice to be what an operator expects here.
    /// </summary>
    public string Voice { get; set; } = "";
}
