namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F141.2 (STORY-355, PLAN T326) — how honestly a <see cref="SegmentKind.TimeDate"/> segment can
/// speak the hour it was armed for, judged against the 90-second honesty threshold.
/// <c>Orchestrator</c>'s drain arm computes this ONCE, at drain time — the SAME air-time-lateness
/// formula <c>SpeechDeferralQueue.TryDequeueDue</c>'s own budget-expiry check uses (real now plus
/// already-queued runtime, minus the armed hour) — and stamps the result onto the
/// <see cref="SegmentRequest"/> it Kicks; never re-derived downstream. <c>PatterTemplateRenderer</c>
/// reads it to choose copy.
///
/// <para>
/// <b>Two values, not three (review round-2 finding F3):</b> a <c>TimeDate</c> deferral drained past
/// the live <c>Station:Imaging:TimeAnnouncementBudgetSeconds</c> budget never reaches this stamp at
/// all — <c>SpeechDeferralQueue.TryDequeueDue</c>'s own expiry check drops it first (SPEC F124.4/
/// F141.3, unchanged by this feature), before <c>Orchestrator</c> ever builds a
/// <see cref="SegmentRequest"/> for it. An earlier revision of this enum carried a third
/// <c>Expired</c> member for <c>TtsSegmentSource</c>'s own belt-and-suspenders guard against that
/// already-unreachable state; the guard (and the member) were removed once review confirmed no
/// production or test caller could ever stamp it onto a real request — see
/// <c>Story321_LateTimeCheckDies.cs</c>'s own facts for the proof that a past-budget drain drops,
/// with the WARN, upstream of this stamp entirely.
/// </para>
/// </summary>
public enum TimeAnnouncementFreshness
{
    /// <summary>Drained within 90 seconds of the armed hour — the classic F110.3 line airs unchanged.</summary>
    OnTime,

    /// <summary>
    /// Drained more than 90 seconds past the armed hour, but still inside the live budget (SPEC
    /// F141.1) — the honest "just past" variant airs instead of the classic line.
    /// </summary>
    Late,
}
