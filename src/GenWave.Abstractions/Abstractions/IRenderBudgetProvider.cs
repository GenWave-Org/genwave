namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F44.2's precedent applied to the TTS render budget (closes gitea-#197): the thin accessor seam
/// between <c>GenWave.Orchestration</c> (which references only <c>GenWave.Core</c> and
/// cannot see the Host's <c>IOptionsMonitor&lt;T&gt;</c> / <c>GenWave.Tts.TtsOptions</c>) and the
/// Host's live configuration. Mirrors <see cref="ICadenceProvider"/>/<see cref="IRotationSettingsProvider"/>
/// one seam over: <c>Tts:RenderBudgetSeconds</c> is advertised <c>Live</c> in the settings allowlist,
/// so a boot-frozen <see cref="TimeSpan"/> captured once at composition-root time (the previous
/// design) would be the same class of bug F30/gitea-#211 fixed for scope/cadence/rotation.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result in
/// a field — so a live edit is visible to the very next TTS render with no process restart. The Host
/// implementation wraps <c>IOptionsMonitor&lt;TtsOptions&gt;</c>, which already is the cache; this
/// interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface IRenderBudgetProvider
{
    /// <summary>The current TTS render budget, evaluated fresh on every call.</summary>
    TimeSpan Current { get; }
}
