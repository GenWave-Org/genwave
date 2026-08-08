// STORY-299 — Weather through the seam (F108, gh-#267)

namespace GenWave.Context.Tests.Specs;

public static class FeatureWeatherProvider
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioConditionsOnCadence
    {
        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void ValidCoordinatesYieldSegmentFactsAndAPatterFact()
        {
            // Fake Open-Meteo handler returns a canned current-conditions payload;
            // provider yields ContextContent with segment facts + a compact patter fact.
            // Assert.NotNull(content.PatterFact);
            Assert.Fail("pending T227");
        }

        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void TheSpokenNameIsTheOnlyLocationString()
        {
            // Facts mention Station:Location:SpokenName; latitude/longitude appear in NO
            // produced string (facts, patter fact, log lines).
            // Assert.DoesNotContain(coordinateDigits, allProducedText);
            Assert.Fail("pending T227");
        }

        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void BlankSpokenNameSpeaksNoPlaceName()
        {
            // Blank SpokenName ⇒ facts carry conditions with no place name at all.
            // Assert.DoesNotContain("in ", content.SegmentFacts); // no locality phrase
            Assert.Fail("pending T227");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fail-closed (F108.1) and outage (F108.4)
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailClosedOnConfiguration
    {
        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void EnabledWithBlankCoordinatesNeverFetches()
        {
            // Enabled=true, blank/invalid lat/lon ⇒ zero HTTP calls, one Information line.
            // Assert.Equal(0, fakeHandler.CallCount);
            Assert.Fail("pending T227");
        }

        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void AnOpenMeteoOutageReturnsNull()
        {
            // Handler throws/times out ⇒ provider returns null (skip semantics upstream);
            // no exception escapes.
            // Assert.Null(content);
            Assert.Fail("pending T227");
        }
    }
}
