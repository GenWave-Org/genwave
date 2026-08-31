// STORY-374 — RotKindTokens/RotStateTokens: the ONE enum↔token map (SPEC F153.9 · PLAN T377)
//
// BDD specification — xUnit. T377 review BLOCKING: GardenerController and Garden.RotFindingRepository
// used to each carry their own RotKind/RotState <-> snake_case switch (five independent copies total)
// — collapsed into these two Core-level token maps, mirroring ImagingKindTokens. Pure value checks,
// no I/O: every enum value must round-trip ToToken -> TryParse back to itself. A future enum value
// added to RotKind/RotState without a matching case still compiles (ToToken's own switch keeps a
// discard arm that throws ArgumentOutOfRangeException) but is caught at RUNTIME instead: Tokens's own
// static initializer calls ToToken on every value, so the missing case surfaces immediately as a
// TypeInitializationException on first access, and this round-trip fact goes red the same way.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureRotKindAndStateTokens
{
    public sealed class ScenarioRotKindRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<RotKind>(), kind =>
            {
                var token = RotKindTokens.ToToken(kind);
                Assert.True(RotKindTokens.TryParse(token, out var parsed) && parsed == kind,
                    $"{kind} did not round-trip through token '{token}'");
            });
        }
    }

    public sealed class ScenarioRotStateRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<RotState>(), state =>
            {
                var token = RotStateTokens.ToToken(state);
                Assert.True(RotStateTokens.TryParse(token, out var parsed) && parsed == state,
                    $"{state} did not round-trip through token '{token}'");
            });
        }
    }
}
