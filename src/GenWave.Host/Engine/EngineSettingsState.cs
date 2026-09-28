namespace GenWave.Host.Engine;

/// <summary>
/// Whether the running engine's tuning (crossfade/safe-gap) matches GenWave's effective
/// configuration (SPEC F213.6, STORY-488). Serialized on <c>/api/status</c> as
/// <c>"inSync"</c>/<c>"restartNeeded"</c>/<c>"unknown"</c> (T602) — never the raw enum name.
/// </summary>
enum EngineSettingsState
{
    /// <summary>All three tuning values match. Nothing to do.</summary>
    InSync,

    /// <summary>At least one tuning value differs from the running engine's — either a fallback
    /// boot (SPEC F213.1) or a saved settings edit still awaiting its <c>EngineRestart</c> apply
    /// mode. The fix is the same either way: <c>docker compose restart engine</c>.</summary>
    RestartNeeded,

    /// <summary>The engine could not be asked (unreachable, timed out) or its reply did not parse
    /// as the three expected numeric keys.</summary>
    Unknown,
}
