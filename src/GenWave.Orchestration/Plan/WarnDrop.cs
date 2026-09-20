namespace GenWave.Orchestration;

/// <summary>
/// The slot drops with a WARN log line naming the kind and cause — today's assignment (PLAN T521)
/// for exactly two kinds: an announcement (naming its claimed id) and a context segment (naming its
/// provider key). Every other kind either drops silently (<see cref="SilentDrop"/>) or also
/// publishes a station event (<see cref="WarnAndEventDrop"/>).
/// </summary>
/// <param name="Subject">
/// Which WARN line to log (SPEC F192.2, PLAN T536) — <see cref="AnnouncementWarnSubject"/> or
/// <see cref="ContextProviderWarnSubject"/>, whichever kind claimed this slot. Required (PLAN T536
/// review finding F4): an optional default here would let a subject-less <see cref="WarnDrop"/> reach
/// a live drop, which <see cref="BreakDelivery.ReportDrop"/> cannot report — every construction site,
/// test or production, names its subject explicitly.
/// </param>
public sealed record WarnDrop(WarnDropSubject Subject) : DropPolicy;
