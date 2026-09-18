namespace GenWave.Orchestration;

/// <summary>The slot drops with no log line at all — reserved for a future slot kind that is safe to skip silently; SPEC F186 names none today.</summary>
public sealed record SilentDrop : DropPolicy;
