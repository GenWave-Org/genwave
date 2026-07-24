namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F87.6 (STORY-227, PLAN T90) — the thin accessor seam between <c>GenWave.Orchestration</c>
/// (which references only <c>GenWave.Core</c> and cannot see the Host's
/// <c>IOptionsMonitor&lt;T&gt;</c> / <c>StationOptions</c>) and the Host's live
/// <c>Station:Requests:OverrideEnvelope</c> setting. Mirrors <see cref="IBoundaryBiasProvider"/> one
/// seam over: a single primitive knob, read fresh on every call rather than cached.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result in a
/// field — so a live edit is visible to the very next fulfillment attempt with no process restart.
/// The Host implementation wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c>, which already is the
/// cache; this interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface IRequestOverrideEnvelopeProvider
{
    /// <summary>
    /// <see langword="true"/> (the default) when a matched/vibe request should bypass the active
    /// envelope's genre/energy constraint at fulfillment time; <see langword="false"/> when the
    /// candidate must also satisfy it. Evaluated fresh on every call. Never governs the canonical
    /// selectability law (ready/measurable/eligible/not-never-play) — that is checked unconditionally
    /// regardless of this value (SPEC F87.6).
    /// </summary>
    bool Current { get; }
}
