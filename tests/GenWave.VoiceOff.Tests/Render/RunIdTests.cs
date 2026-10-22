using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class RunIdTests
{
    [Fact]
    public void ItIsTheUtcStartAndTheTier() =>
        Assert.Equal("20261022T193000Z-cpu", RunId.Format(new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero), EngineTier.Cpu));

    [Fact]
    public void ALocalOffsetIsConvertedToUtc() =>
        Assert.Equal("20261022T193000Z-gpu", RunId.Format(new DateTimeOffset(2026, 10, 22, 21, 30, 0, TimeSpan.FromHours(2)), EngineTier.Gpu));
}
