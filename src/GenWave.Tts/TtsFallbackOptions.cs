namespace GenWave.Tts;

/// <summary>
/// Configuration for the TTS fallback resiliency chain (SPEC F70.1, STORY-190, gh-#147).
///
/// Two shapes coexist under <c>Tts:Fallback</c>:
/// <list type="bullet">
/// <item><see cref="Profiles"/> — the gh-#147 operator-built chain: an ordered array of
/// <see cref="TtsFallbackProfile"/> hops (engine kind, endpoint, voice, per-hop probe/timeout
/// semantics), tried in order after the primary. Deployment config only (appsettings/env) —
/// validated loudly at startup by <see cref="TtsFallbackOptionsValidator"/>.</item>
/// <item><see cref="Endpoint"/>/<see cref="Voice"/> — the legacy flat keys (STORY-190,
/// live-editable through PUT /api/settings). When <see cref="Profiles"/> is absent/empty these
/// form the implicit legacy chain: exactly one piper hop, behavior-identical to the pre-gh-#147
/// single Kokoro→Piper hop — the shipped compose.yaml sets only these, so a bare deploy is
/// unchanged. When <see cref="Profiles"/> is configured, the flat keys are ignored.</item>
/// </list>
/// <see cref="TtsFallbackChain.Resolve"/> is the single reconciliation point. Everything empty is
/// the disabled state: <see cref="FallbackTtsSynthesizer"/> then routes every render straight to
/// the primary (Kokoro) with no health read and no retry — exactly pre-T34 behavior.
/// </summary>
public sealed class TtsFallbackOptions
{
    public const string Section = "Tts:Fallback";

    /// <summary>
    /// LEGACY single-hop shape: Piper HTTP wrapper base URL. Empty = no implicit legacy chain
    /// (F70.1's disabled state). Ignored when <see cref="Profiles"/> is non-empty.
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// LEGACY single-hop shape: the Piper voice model the fallback sidecar is expected to be
    /// running (e.g. <c>"en_US-lessac-medium"</c>) — operator-facing documentation of what
    /// <c>MODEL_DOWNLOAD_LINK</c> the compose <c>piper</c> service was started with. The upstream
    /// <c>piper.http_server</c> wrapper bakes exactly one voice model into the running container
    /// and exposes no per-request voice selector, so this value is never sent on the wire
    /// (<see cref="PiperTtsSynthesizer"/> does not read it) — it only needs to match compose.yaml's
    /// <c>piper</c> service for the deployed voice to be what an operator expects here. In the
    /// gh-#147 chain shape this becomes <see cref="TtsFallbackProfile.Voice"/>, whose semantics
    /// are per engine kind (see its own remarks).
    /// </summary>
    public string Voice { get; set; } = "";

    /// <summary>
    /// The gh-#147 ordered fallback chain — hop 0 is tried first after the primary. Empty
    /// (default) defers to the legacy flat keys above; see the class remarks for the full
    /// precedence.
    /// </summary>
    public IList<TtsFallbackProfile> Profiles { get; set; } = [];
}
