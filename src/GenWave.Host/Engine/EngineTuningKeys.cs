namespace GenWave.Host.Engine;

/// <summary>
/// The three <c>IConfiguration</c>-backed keys that define the engine's crossfade/safe-gap tuning
/// (SPEC F213.5) — the ONE source both <see cref="Api.InternalEndpoints"/>'
/// <c>/internal/engine-config</c> body (what the engine boots with) and
/// <see cref="EngineSettingsVerdict"/> (what the running engine reports back via <c>gw_tuning</c>)
/// key off, so the two can never silently drift apart. <see cref="EngineSettingsVerdict.Differs"/>
/// is ordered exactly as this list.
/// <para>
/// GW_SAFE_GAP_SECONDS rides the same path as GW_XFADE_MIN/MAX (F29.8, STORY-100): it must appear
/// here or a PUT /api/settings override would persist to the overlay but never reach the engine on
/// its next boot.
/// </para>
/// </summary>
static class EngineTuningKeys
{
    public static readonly IReadOnlyList<string> All =
        ["GW_XFADE_MIN", "GW_XFADE_MAX", "GW_SAFE_GAP_SECONDS"];
}
