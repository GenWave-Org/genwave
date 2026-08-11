namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F116.3 (STORY-308, PLAN T249) — the thin accessor seam between
/// <c>GenWave.Orchestration.ShowFlavorLineGate</c> (which cannot see the Host's
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c> directly) and the Host's live
/// <c>Station:Shows:PatterCadenceMinutes</c> value. Mirrors <see cref="ICadenceProvider"/> one seam
/// over: <c>Station:Shows:PatterCadenceMinutes</c> is advertised <c>Live</c> in the settings allowlist,
/// so an operator edit reaches the very next eligible break with no process restart.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="PatterCadenceMinutes"/> fresh on every read — never
/// cache it in a field — the same discipline <see cref="ICadenceProvider.Current"/> follows. No NoOp
/// default is registered anywhere: this seam has exactly one consumer (<c>ShowFlavorLineGate</c>),
/// which is itself only ever bound by the Host in the SAME registration that supplies the real
/// <c>OptionsMonitorShowPatterCadenceProvider</c> binding (mirrors <see cref="ICadenceProvider"/>'s own
/// mandatory-dependency posture on <c>Orchestrator</c> — no fallback exists for that seam either).
/// </para>
/// </summary>
public interface IShowPatterCadenceProvider
{
    /// <summary>The live cadence, in minutes, at which the show-flavor patter line may air per show
    /// (SPEC F116.3); 0 disables it (the default — an opt-in feature). Evaluated fresh on every
    /// call.</summary>
    int PatterCadenceMinutes { get; }
}
