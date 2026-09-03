// gh-#380 — ImagingKindTokens: the ONE enum<->token map (SPEC F158.1 · PLAN T395, carry-forward from
// T390 r2 review: ToToken(ImagingKind.Ad) THREW before this task added the fifth arm).
//
// BDD specification — xUnit. Mirrors Story374_RotKindAndStateTokens.cs's own precedent exactly (the
// Story006_PatterTemplates.cs "walk every enum value automatically, don't name them by hand" idea,
// applied here as the SAME Assert.All round-trip shape Story374/Story379 already established in this
// project): pure value checks, no I/O — every ImagingKind value must round-trip ToToken -> TryParse
// back to itself. The NEXT imaging kind appended after Ad is caught here automatically instead of
// relying on its author remembering to add a case to both ToToken and TryParse.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureImagingKindTokensRoundTrip
{
    public sealed class ScenarioEveryImagingKindRoundTrips
    {
        [Fact]
        public void EveryEnumValueRoundTripsThroughItsToken()
        {
            Assert.All(Enum.GetValues<ImagingKind>(), kind =>
            {
                var token = ImagingKindTokens.ToToken(kind);
                Assert.True(ImagingKindTokens.TryParse(token, out var parsed) && parsed == kind,
                    $"{kind} did not round-trip through token '{token}'");
            });
        }
    }
}
