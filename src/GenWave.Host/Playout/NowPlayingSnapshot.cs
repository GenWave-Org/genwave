namespace GenWave.Host.Playout;

/// <summary>
/// Immutable Host-layer read model of what is currently on-air for a station. Built from
/// <see cref="GenWave.Core.Playout.OnAirState"/> after each feeder tick and stored in
/// <see cref="NowPlayingService"/>. Served directly by the API — no engine telnet calls at
/// request time.
/// </summary>
/// <param name="MediaId">Null when a drain token is on-air.</param>
/// <param name="Title">Track title, if known.</param>
/// <param name="Artist">Track artist, if known.</param>
/// <param name="GainDb">Applied loudness-normalisation gain.</param>
/// <param name="StartedAt">UTC wall-clock instant when the current on-air item was detected.</param>
/// <param name="DurationMs">
/// Track duration, if known (SPEC F50.3). <c>tts:*</c> patter carries its measured duration (SPEC
/// F66.1); an engine-initiated play starts null and is patched in place once <see cref="DurationRehydrator"/>
/// recovers it from the catalog (SPEC F66.2) — never fabricated.
/// </param>
/// <param name="IsDrain">True when the safe-rotation/drain token is on-air (no real track).</param>
public sealed record NowPlayingSnapshot(
    string? MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs,
    bool IsDrain);
