namespace GenWave.Tts;

/// <summary>
/// Point-in-time result of <see cref="DegradationController.Evaluate"/> (SPEC F69.5, STORY-188) —
/// what every mode-transition log line reflects and what <c>GET /api/status</c> surfaces.
/// </summary>
/// <param name="Mode">The current degradation mode.</param>
/// <param name="Pinned">
/// True while an operator-set <see cref="LlmOptions.DegradationPin"/> holds <paramref name="Mode"/>
/// — auto drop/raise is suspended until it is reset back to <c>"auto"</c> (SPEC F69.3).
/// </param>
/// <param name="Since">When <paramref name="Mode"/> was last entered.</param>
/// <param name="Cause">Human-readable reason for the current mode (SPEC F69.5).</param>
public sealed record DegradationSnapshot(DegradationMode Mode, bool Pinned, DateTimeOffset Since, string Cause);
