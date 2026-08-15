namespace GenWave.Tts;

/// <summary>
/// The operator-facing half of F99.5/F100.3 (PLAN T149, STORY-256 AC4): reduces the primary voice
/// engine's cached <see cref="IDependencyHealth"/> verdict to the single fact an operator with no
/// log stack needs — "is the DJ silent because the engine is down". Reads
/// <see cref="PrimaryVoiceEngine"/> rather than a hardcoded Kokoro name, so the piper-only
/// topology (SPEC F99.4) reports on the engine that is ACTUALLY primary there (the same T148
/// review finding F3 discipline the render-time gate already follows) — and, deliberately, NOT
/// <see cref="FallbackTtsSynthesizer"/> itself: that class's primary is an HTTP-backed typed
/// client, and resolving it as a side effect of a status read would give <c>GET /api/status</c> an
/// <see cref="System.Net.Http.IHttpClientFactory"/> dependency it must never have (STORY-125 AC3;
/// see <see cref="PrimaryVoiceEngine"/>'s own remarks). Zero I/O — the same cached-read discipline
/// every <see cref="IDependencyHealth"/> consumer follows (<see cref="DegradationController.Evaluate"/>'s
/// own remarks), so <c>GET /api/status</c> can call <see cref="Evaluate"/> on every poll for free.
/// <para>
/// Deliberately silent about copy availability (LLM degradation mode, TemplateCopyWriter
/// fallback, the not-authored-copy render guard) — that is "the DJ has nothing to say", a
/// different cause the same status response already carries under <c>degradation</c>
/// (<see cref="DegradationController"/>). This reader answers only the engine-down half, so the
/// two facts can sit side by side on the wire without ever colliding on one field.
/// </para>
/// </summary>
public sealed class VoiceHealthReader(PrimaryVoiceEngine primary, IDependencyHealth health)
{
    public VoiceHealthSnapshot Evaluate()
    {
        var engine = primary.DependencyName;
        var verdict = health.GetVerdict(engine);

        return verdict is { Healthy: false } unhealthy
            ? new VoiceHealthSnapshot(engine, Degraded: true, unhealthy.Reason, unhealthy.CheckedAt)
            : new VoiceHealthSnapshot(engine, Degraded: false, Reason: null, verdict?.CheckedAt);
    }
}
