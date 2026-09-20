namespace GenWave.Orchestration;

/// <summary>
/// What a <see cref="WarnDrop"/> names in its WARN line (SPEC F192.2, PLAN T536) — a closed
/// hierarchy of exactly two cases, each declared in its own sibling file:
/// <see cref="AnnouncementWarnSubject"/>, <see cref="ContextProviderWarnSubject"/>. Carrying the
/// subject on the policy itself (rather than <see cref="BreakDelivery"/> re-deriving it from the
/// slot's <see cref="SegmentKind"/>) means the drop-report switch dispatches on ONE thing, not two.
/// </summary>
public abstract record WarnDropSubject
{
    private protected WarnDropSubject() { }
}
