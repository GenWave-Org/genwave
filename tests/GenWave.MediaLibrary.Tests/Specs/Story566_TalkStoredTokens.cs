// STORY-566 — Every attempt is recorded (SPEC F220.29 · PLAN T779)
//
// BDD specification — xUnit, no database: the stored spelling of a talk kind.

using GenWave.Core.TalkRules;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkStoredTokens
{
    // Sad path — a value with no single token
    public sealed class ScenarioAKindHasNoSingleToken
    {
        [Fact]
        public void NoneIsRefusedWithAClearMessage() =>
            Assert.Contains("no single stored token", Assert.Throws<ArgumentException>(() => TalkStoredTokens.Of(TalkKinds.None)).Message);
    }
}
