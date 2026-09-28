namespace GenWave.Host.Engine;

/// <summary>
/// Narrow seam (SPEC F213.5) that reads the engine's live tuning values off the <c>gw_tuning</c>
/// telnet command (<c>engine/genwave.liq</c>, PLAN T600). Deliberately NOT a member of Core's
/// <see cref="GenWave.Core.Abstractions.ILiquidsoapControl"/>: that interface is the on-air
/// push/metadata contract every playout caller depends on, while an engine-settings verdict is a
/// Host-only diagnostic (SPEC F213) with no Core caller — folding it in would widen a stable seam
/// for one narrow reader.
/// </summary>
interface IEngineTuningReader
{
    /// <summary>
    /// Returns the raw <c>gw_tuning</c> reply line verbatim (e.g.
    /// <c>"GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0"</c>) — unparsed;
    /// <see cref="EngineSettingsVerdict.Compute"/> owns parsing and comparison. Throws (never
    /// returns null or an empty string to signal failure) when the engine cannot be reached or the
    /// read does not complete in time — <see cref="EngineSettingsVerdict.ComputeAsync"/> is where
    /// that throw is caught and turned into <see cref="EngineSettingsState.Unknown"/> (SPEC F213.6,
    /// AC9).
    /// </summary>
    Task<string> ReadAsync(CancellationToken ct);
}
