namespace GenWave.Core.Domain;

/// <summary>
/// The two roles a <see cref="CrosstalkAiredScript"/> line was ever cast to (SPEC F127.1, F127.2,
/// STORY-326) — never a third. The host is the on-air DJ; the neighbor is the schedule-adjacent
/// "drop-in" persona <c>GenWave.Orchestration.CrosstalkPlanner</c> resolves from the grid.
///
/// <para>
/// <b>Lives in GenWave.Abstractions, not GenWave.Tts (review finding F8, round-2).</b>
/// <c>GenWave.Tts.CrosstalkScriptParser</c> is this enum's one producer, but
/// <c>GenWave.Orchestration</c>/<c>GenWave.MediaLibrary</c> — the projects that carry a validated
/// script forward onto <see cref="CrosstalkAiredScript"/>/<c>MediaItem.CrosstalkScript</c>/
/// <c>TrackAired.CrosstalkScript</c> and, from there, into the booth log's own <c>pick</c> jsonb stamp
/// — cannot reference <c>GenWave.Tts</c> at all (the epic's own "planning stays decoupled from
/// script/render" posture, ARCHITECTURE.md's Crosstalk section). A second, string-typed
/// <c>CrosstalkAiredLine.Speaker</c> existed for exactly that reason before this fix; moving the enum
/// itself here instead lets <see cref="CrosstalkAiredLine"/> carry it directly — ONE shape, produced
/// and consumed by both sides, rather than a duplicate string mirror needing its own hand-written
/// mapper. Additive here (a new published Abstractions type, minor version, no binary break) — nothing
/// in the shipped 5.0.0 surface references it.
/// </para>
/// </summary>
public enum CrosstalkSpeaker
{
    /// <summary>The on-air host persona.</summary>
    Host,

    /// <summary>The schedule-adjacent drop-in persona (SPEC F127.2).</summary>
    Neighbor,
}
