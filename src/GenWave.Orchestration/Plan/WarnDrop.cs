namespace GenWave.Orchestration;

/// <summary>
/// The slot drops with a WARN log line naming the kind and cause — today's assignment (PLAN T521)
/// for exactly two kinds: an announcement (naming its claimed id) and a context segment (naming its
/// provider key). Every other kind either drops silently (<see cref="SilentDrop"/>) or also
/// publishes a station event (<see cref="WarnAndEventDrop"/>).
/// </summary>
public sealed record WarnDrop : DropPolicy;
