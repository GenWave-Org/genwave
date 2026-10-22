using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class TimingRecordTests
{
    internal static TimingRecord Record(double wallMs = 2000, double audioMs = 4000, bool ok = true) =>
        new("alpha", RenderLane.Plain, "hard-01", EngineTier.Cpu, wallMs, audioMs, 1229, null, ok, null);

    [Fact]
    public void NoAudioMeansNoRtfNotInfinity() => Assert.Null(Record(audioMs: 0).Rtf);
}
