namespace GenWave.Core.Playout;

/// <summary>
/// Snapshot of the feeder's on-air state captured after each tick. Exposed by
/// <see cref="PlayoutFeeder.CurrentOnAir"/> for the Host layer to read without coupling Core to
/// any Host or infrastructure type. All fields are primitives — no framework deps cross this seam.
/// </summary>
/// <param name="MediaId">The stamped media id currently on-air. Null when drain or not yet ticked.</param>
/// <param name="Title">Track title from pushed metadata, if known.</param>
/// <param name="Artist">Track artist from pushed metadata, if known.</param>
/// <param name="GainDb">Applied loudness-normalisation gain (0.0 when drain/cold).</param>
/// <param name="StartedAt">UTC instant the current on-air item was first detected.</param>
/// <param name="DurationMs">
/// Track duration from pushed metadata, if known (SPEC F50.2). <c>tts:*</c> patter carries its
/// measured cue-derived duration here (SPEC F66.1); an engine-initiated play is always null at this
/// layer — the Host rehydrates it from the catalog after publish (SPEC F66.2), since
/// <see cref="PlayoutFeeder"/> stays DB-free (F16.6). Never fabricated.
/// </param>
/// <param name="IsReal">True when the on-air item carries our stamped media id (not a drain token).</param>
/// <param name="IsReady">False until the feeder has completed at least one tick.</param>
/// <param name="ArtworkUrl">
/// The F88 token artwork URL from pushed metadata, if known (SPEC F88.4, F93.3) — the same value the
/// engine push's <c>url=</c> annotation carried, threaded through <c>pushedMeta</c> at push time
/// (feeder-pushed) or recovered from the echoed output metadata (engine-initiated); never a fresh
/// lookup at this layer (<see cref="PlayoutFeeder"/> stays DB-free, F16.6/F93.4). An engine-initiated
/// echo is gated through <c>IArtworkUrlEchoValidator</c> before it ever reaches here (SPEC F88.5,
/// PLAN T125 review F2), so a hostile file tag can never surface as this field. Null when no art, no
/// <c>Station:PublicBaseUrl</c>, an untrusted echo, or not yet known.
/// </param>
public sealed record OnAirState(
    string? MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs,
    bool IsReal,
    bool IsReady,
    string? ArtworkUrl = null);
