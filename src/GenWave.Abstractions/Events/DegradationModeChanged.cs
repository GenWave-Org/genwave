namespace GenWave.Core.Events;

/// <summary>
/// The LLM degradation ladder moved one step (SPEC F69.1-F69.5, F72.1; STORY-188, STORY-195).
/// <paramref name="Previous"/>/<paramref name="New"/> are the mode names ("Normal", "Soft", "Hard") —
/// plain strings, not <c>GenWave.Tts.DegradationMode</c>, so this contract stays dependency-free
/// (this project ships with zero dependencies; the enum lives in a project this one cannot
/// reference). <paramref name="Cause"/> is the same human-readable reason
/// <c>DegradationController</c> already logs on every transition.
/// </summary>
public sealed record DegradationModeChanged(string Previous, string New, string Cause) : StationEvent;
