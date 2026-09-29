namespace GenWave.Host.Engine;

/// <summary>
/// The one cache <c>GET /api/status</c> reads for the engine-settings verdict (SPEC F213.5-F213.6,
/// PLAN T602) — <see cref="EngineSettingsCheck"/> is the sole writer, on the dependency-health probe
/// cadence; <c>StatusController</c> only ever reads <see cref="Settings"/>/<see cref="Differs"/>,
/// never calling <see cref="IEngineTuningReader"/> itself (AC6). <see cref="Settings"/> is
/// <c>"unknown"</c> and <see cref="Differs"/> is empty until the first probe completes (AC11).
/// <para>
/// Deliberately <see langword="public"/>, unlike <see cref="EngineSettingsVerdict"/> and
/// <see cref="EngineSettingsState"/> (both internal by design — SPEC F213.9, no seam widening for a
/// Host-only diagnostic): <c>StatusController</c> is a public class, and a public constructor may
/// not take a parameter of a less-accessible type (CS0051). This is the one point where the
/// internal engine-settings verdict crosses into something a public constructor can reference — and
/// it crosses already projected onto exactly the two fields AC12 allows onto the wire (the state's
/// name and the differing key names), never a raw value.
/// </para>
/// <para>
/// <see cref="Set"/> swaps one immutable <see cref="Snapshot"/> reference under a
/// <see langword="volatile"/> field, so <see cref="Settings"/>/<see cref="Differs"/> can never be
/// read as a torn combination from two different probe cycles (mirrors
/// <c>Api.SchemaVersionStatus</c>'s own "write once per cycle, read many times, no lock" shape one
/// seam over).
/// </para>
/// </summary>
public sealed class EngineSettingsStatus
{
    static readonly Snapshot Initial = new("unknown", []);

    volatile Snapshot current = Initial;

    /// <summary><c>"inSync"</c>, <c>"restartNeeded"</c>, or <c>"unknown"</c> — the SAME camelCase
    /// names <c>StatusController</c> already uses for every other enum-shaped field on the
    /// response.</summary>
    public string Settings => current.Settings;

    /// <summary>The <see cref="EngineTuningKeys.All"/> entries that differ — key names only, never a
    /// value (AC12). Always an array, empty when there is nothing to report.</summary>
    public IReadOnlyList<string> Differs => current.Differs;

    /// <summary>Called by <see cref="EngineSettingsCheck"/> after every probe cycle.</summary>
    internal void Set(EngineSettingsVerdict verdict) =>
        current = new Snapshot(ToWireName(verdict.State), verdict.Differs);

    static string ToWireName(EngineSettingsState state) => state switch
    {
        EngineSettingsState.InSync => "inSync",
        EngineSettingsState.RestartNeeded => "restartNeeded",
        EngineSettingsState.Unknown => "unknown",
        _ => "unknown",
    };

    sealed record Snapshot(string Settings, IReadOnlyList<string> Differs);
}
