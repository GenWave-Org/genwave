namespace GenWave.Orchestration;

/// <summary>
/// The slot drops with a WARN log line naming the kind and cause — the default reporting SPEC
/// F186.3 describes for an announcement (its id), a context segment (its provider key), and "the
/// existing line" for everything else.
/// </summary>
public sealed record WarnDrop : DropPolicy;
