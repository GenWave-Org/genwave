// STORY-379 — FileActionVerbTokens/FileActionOutcomeTokens/FileActionRuleTokens: the ONE enum<->token
// map (SPEC F154.1, F154.3, F154.7 · PLAN T380 review N6, round 3 item 1)
//
// BDD specification — xUnit. Mirrors Story374_RotKindAndStateTokens.cs's own precedent exactly: pure
// value checks, no I/O — every enum value must round-trip ToToken -> TryParse back to itself.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFileActionTokens
{
    public sealed class ScenarioFileActionVerbRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<FileActionVerb>(), verb =>
            {
                var token = FileActionVerbTokens.ToToken(verb);
                Assert.True(FileActionVerbTokens.TryParse(token, out var parsed) && parsed == verb,
                    $"{verb} did not round-trip through token '{token}'");
            });
        }
    }

    public sealed class ScenarioFileActionOutcomeRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<FileActionOutcomeKind>(), kind =>
            {
                var token = FileActionOutcomeTokens.ToToken(kind);
                Assert.True(FileActionOutcomeTokens.TryParse(token, out var parsed) && parsed == kind,
                    $"{kind} did not round-trip through token '{token}'");
            });
        }
    }

    public sealed class ScenarioFileActionRuleRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<FileActionRule>(), rule =>
            {
                var token = FileActionRuleTokens.ToToken(rule);
                Assert.True(FileActionRuleTokens.TryParse(token, out var parsed) && parsed == rule,
                    $"{rule} did not round-trip through token '{token}'");
            });
        }
    }
}
