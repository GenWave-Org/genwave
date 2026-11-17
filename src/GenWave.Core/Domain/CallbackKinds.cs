namespace GenWave.Core.Domain;

/// <summary>
/// Owner of the F218.40 kind rule: "A <b>callback break</b> is a <c>LeadIn</c> or <c>BackAnnounce</c> whose prompt
/// carries exactly one memory and a direct ask." The planner and the prompt builder both call this, so the rule
/// cannot drift between them.
/// </summary>
public static class CallbackKinds
{
    public static bool Carries(SegmentKind kind) =>
        kind is SegmentKind.LeadIn or SegmentKind.BackAnnounce;
}
