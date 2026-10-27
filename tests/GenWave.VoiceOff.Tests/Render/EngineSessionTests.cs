using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Tests.Render;

public static class EngineSessionTests
{
    public sealed class AGateThatFailedWithoutAReason
    {
        private readonly EngineSession session = EngineSession.Unavailable("  ");

        [Fact]
        public void GetsTheFallbackReason() => Assert.Equal(EngineSession.NoGateReason, session.GateFailure);
    }
}
