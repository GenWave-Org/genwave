namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F107.5 (STORY-298) — the patter lane's own internal seam: at most one compact, cadence-due
/// fact for the on-air copywriter's prompt. Deliberately NOT part of the published MIT
/// <c>GenWave.Abstractions</c> surface (F105.6) — nothing outside this codebase's own patter lane
/// (<c>GenWave.Tts.LlmCopyWriter</c>, PLAN T225) ever needs to consume it, so it lives one layer in,
/// alongside <see cref="IContextSettingsProvider"/>.
///
/// <para>
/// Mirrors <see cref="IContextSettingsProvider"/>'s own reason for existing one seam over
/// (<c>GenWave.Context</c> references only <c>GenWave.Core</c>/<c>GenWave.Abstractions</c> and is
/// never referenced BACK by either): <c>GenWave.Context.ContextPipeline</c> is the one production
/// implementation, and this interface is what lets <c>GenWave.Tts</c> depend on the CONTRACT
/// without ever taking a project reference to <c>GenWave.Context</c> itself — an L1 project one
/// layer further out than either. <see cref="NoOpContextPatterFactSource"/> is the safe default
/// until a host wires the real binding.
/// </para>
/// </summary>
public interface IContextPatterFactSource
{
    /// <summary>
    /// Returns the single due patter fact for right now, or <see langword="null"/> when none is due
    /// — no provider registered, none enabled, none fresh, or this cadence slot already vended one
    /// (SPEC F107.6's skip-never-silence posture: never an error, never a reason to fail a render).
    ///
    /// <b>This is a CONSUMING read, not a peek.</b> A non-null return is marked delivered for its
    /// cadence slot and will not be returned again — calling this twice for the same due fact yields
    /// it once and <see langword="null"/> the second time. A caller that must never spend the slot
    /// (a persona preview, PLAN T225's own CQS-trap guard — see
    /// <c>GenWave.Tts.LlmCopyWriter.WritePreviewAsync</c>'s remarks) must not call this method at
    /// all, rather than calling it and discarding the result — discarding still burns the slot.
    /// </summary>
    ContextPatterFact? TryTakeDuePatterFact();
}
