namespace GenWave.Tts;

/// <summary>
/// Point-in-time result of <see cref="VoiceHealthReader.Evaluate"/> (SPEC F99.5, F100.3,
/// STORY-256 AC4) — what <c>GET /api/status</c> surfaces as <c>voice</c>. Answers ONLY "is the DJ
/// silent because the engine is down"; copy availability ("the DJ has nothing to say") is a
/// separate cause the same status response already carries under <c>degradation</c>
/// (<see cref="DegradationController"/>) — the two facts must never collide on one field.
/// </summary>
/// <param name="Engine">The primary engine's <see cref="DependencyNames"/> key — <c>kokoro</c> on
/// every topology except the piper-only opt-in, where it is <c>piper</c> (SPEC F99.4).</param>
/// <param name="Degraded">True when the cached verdict for <paramref name="Engine"/> is unhealthy.
/// False both when it is healthy and when no probe cycle has completed yet (the brief startup
/// window) — a degraded read is never fabricated ahead of real evidence.</param>
/// <param name="Reason">The verdict's own reason, populated only when <paramref name="Degraded"/>
/// — mirrors <see cref="DependencyHealthVerdict.Reason"/>'s own null-exactly-when-healthy
/// invariant.</param>
/// <param name="CheckedAt">When the verdict was last recorded; null before the first probe cycle
/// completes.</param>
public sealed record VoiceHealthSnapshot(
    string Engine, bool Degraded, string? Reason, DateTimeOffset? CheckedAt);
