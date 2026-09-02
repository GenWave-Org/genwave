using GenWave.Tts;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IDegradationModeReader"/> double — defaults to <see cref="DegradationMode.Normal"/>,
/// mirrors <c>GenWave.Tts.Tests.Fakes.FakeDegradationModeReader</c>'s own shape one project over (PLAN
/// T400 review F2 — the real-Tts-meets-real-Ads crossing fact constructs a real
/// <see cref="AdScriptWriter"/>, which needs this one unrelated constructor dependency satisfied
/// without standing up a full <c>DegradationController</c>).
/// </summary>
public sealed class FakeDegradationModeReader : IDegradationModeReader
{
    public DegradationMode CurrentMode { get; set; } = DegradationMode.Normal;
}
