namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F116.3 (STORY-308, PLAN T249) — the show-flavor patter line's own internal seam: at most one
/// due show line for the on-air copywriter's prompt, mirroring <see cref="IContextPatterFactSource"/>'s
/// own shape and reason for existing one seam over. Deliberately NOT part of the published MIT
/// <c>GenWave.Abstractions</c> surface (F105.6): nothing outside this codebase's own patter lane
/// (<c>GenWave.Tts.LlmCopyWriter</c>, PLAN T249) ever needs to consume it, so it lives one layer in,
/// alongside <see cref="IContextPatterFactSource"/>.
///
/// <para>
/// <c>GenWave.Orchestration.ShowFlavorLineGate</c> is the one production implementation — show
/// identity is Orchestration's own domain (<c>OnAirPersonaAccessor</c>, <c>CachingScheduleResolver</c>,
/// <c>HandoffContext</c> all already live there) — and this interface is what lets
/// <c>GenWave.Tts</c> depend on the CONTRACT without ever taking a project reference to
/// <c>GenWave.Orchestration</c> itself, the exact same L1 reason <see cref="IContextPatterFactSource"/>
/// lives here rather than beside <c>GenWave.Context.ContextPipeline</c>. <see cref="NoOpShowFlavorLineSource"/>
/// is the safe default until a host wires the real binding.
/// </para>
/// </summary>
public interface IShowFlavorLineSource
{
    /// <summary>
    /// Returns the show-flavor line due right now, or <see langword="null"/> when none is due — no
    /// show on the air, the show carries no flavor text, <c>Station:Shows:PatterCadenceMinutes</c> is
    /// 0 (off) or has not yet elapsed for THIS show, or the caller simply never asks (SPEC F116.3's own
    /// arbitration: "context wins... the show gate stays open for the next eligible break" — never a
    /// reason to fail a render).
    ///
    /// <b>This is a CONSUMING read, not a peek.</b> A non-null return is marked delivered for its
    /// show's cadence window and will not be returned again until that window elapses — calling this
    /// twice in immediate succession for the same due show yields it once and <see langword="null"/>
    /// the second time. A caller that must not spend the slot (SPEC F116.3's own "context wins" —
    /// see <c>GenWave.Tts.LlmCopyWriter.TakeDueShowFlavorLineForOnAirRender</c>'s remarks) must not call
    /// this method at all, rather than calling it and discarding the result — discarding still burns
    /// the slot.
    /// </summary>
    ShowFlavorFact? TryTakeDueShowLine();
}
