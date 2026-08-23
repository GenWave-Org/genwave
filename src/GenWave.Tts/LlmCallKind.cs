namespace GenWave.Tts;

/// <summary>
/// Which generation surface produced an <see cref="LlmCallRing"/> entry (SPEC F73.1, F127.11, F144.3,
/// PLAN T282, T342) — orthogonal to <see cref="LlmCallOutcome"/> (what happened) and to
/// <see cref="DegradationMode"/> (what the ladder was doing at the time). Every call
/// <see cref="LlmCopyWriter"/> itself records — on-air, Soft-cadence, or an operator preview alike —
/// is <see cref="Copy"/>; <see cref="CrosstalkScriptWriter"/>'s own completion calls are
/// <see cref="Crosstalk"/>, so an operator reading <c>/api/llm-calls</c> can tell "why was there no
/// banter" apart from an ordinary blurb miss without re-deriving it from the prompt text (F127.11).
/// <see cref="Announcement"/> (PLAN T342) is the SAME split one lane over: an owner announcement's own
/// flavored-copy attempt (<see cref="LlmCopyWriter.WriteAnnouncementAsync"/>) stamps this instead of
/// <see cref="Copy"/>, so a <see cref="LlmCallCause.TruthGateReject"/> caused by a dropped announcement
/// core is visible as its OWN lane on the F139 bench/cause surface, never folded into ordinary
/// LeadIn/BackAnnounce copy noise.
/// </summary>
public enum LlmCallKind
{
    /// <summary>An ordinary segment-copy completion (<see cref="LlmCopyWriter"/>) — LeadIn, BackAnnounce, SignOff, SignOn, ContextSegment, or a persona preview.</summary>
    Copy,

    /// <summary>A two-voice banter script completion (<see cref="CrosstalkScriptWriter"/>, SPEC F127.3).</summary>
    Crosstalk,

    /// <summary>An owner announcement's flavored-copy completion (<see cref="LlmCopyWriter.WriteAnnouncementAsync"/>, SPEC F144.3, PLAN T342).</summary>
    Announcement,
}
