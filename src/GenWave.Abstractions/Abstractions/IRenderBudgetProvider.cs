namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F44.2's precedent applied to the TTS render budget (closes gitea-#197): the thin accessor seam
/// between <c>GenWave.Orchestration</c> (which references only <c>GenWave.Core</c> and
/// cannot see the host's configuration types) and the live render budget. Mirrors
/// <see cref="ICadenceProvider"/>/<see cref="IRotationSettingsProvider"/> one seam over: the budget
/// can change at runtime, so a boot-frozen <see cref="TimeSpan"/> captured once at composition-root
/// time would be the same class of bug F30/gitea-#211 fixed for scope/cadence/rotation.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result in
/// a field — so a live edit is visible to the very next TTS render with no process restart. Any
/// caching belongs to the underlying live configuration source; this interface adds nothing beyond
/// a Core-visible accessor.
/// </para>
/// </summary>
public interface IRenderBudgetProvider
{
    /// <summary>The current TTS render budget, evaluated fresh on every call.</summary>
    TimeSpan Current { get; }
}
