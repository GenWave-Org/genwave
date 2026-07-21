namespace GenWave.Tts.Tests.Fakes;

using GenWave.Tts;

/// <summary>
/// Scriptable <see cref="IDegradationModeReader"/> double (STORY-196, T41) — defaults to
/// <see cref="DegradationMode.Normal"/>, the mode every spec that doesn't itself care about
/// degradation wants stamped onto its <c>LlmCallRing</c> records. Exists specifically so a spec
/// that only exercises <see cref="LlmCopyWriter"/>'s hygiene/fallback/prompt behavior never has to
/// stand up a full <see cref="DegradationController"/> (with its own dependency-health/options/clock/
/// logger fakes) just to satisfy this one unrelated constructor parameter.
/// </summary>
public sealed class FakeDegradationModeReader : IDegradationModeReader
{
    public DegradationMode CurrentMode { get; set; } = DegradationMode.Normal;
}
