namespace GenWave.Orchestration;

/// <summary>
/// The slot drops with a WARN log line AND publishes a station event — today the only drop event
/// is <c>GenWave.Core.Events.HandoffPieceDropped</c>, published for a dropped sign-off/sign-on
/// (SPEC F186.3, F92.4). Declared as a parameterless case, not <c>WarnAndEvent(EventKind)</c>: no
/// <c>EventKind</c> enum exists yet, and inventing one for a single event would be pure
/// speculation (YAGNI) — a second drop event would need this case to grow a discriminator, not the
/// hierarchy to grow a case.
/// </summary>
public sealed record WarnAndEventDrop : DropPolicy;
