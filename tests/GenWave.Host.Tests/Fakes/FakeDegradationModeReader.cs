namespace GenWave.Host.Tests.Fakes;

using GenWave.Tts;

/// <summary>
/// Scriptable <see cref="IDegradationModeReader"/> double — defaults to
/// <see cref="DegradationMode.Normal"/>. Mirrors <c>GenWave.Tts.Tests.Fakes.FakeDegradationModeReader</c>
/// exactly; duplicated here rather than referenced because <c>GenWave.Host.Tests</c> has no project
/// reference to <c>GenWave.Tts.Tests</c> (test projects don't reference each other in this codebase).
/// </summary>
sealed class FakeDegradationModeReader : IDegradationModeReader
{
    public DegradationMode CurrentMode { get; set; } = DegradationMode.Normal;
}
