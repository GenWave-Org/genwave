// STORY-183 — Disclosure contract asserts complete property sets
//
// BDD specification — xUnit (SPEC F67.6; amends the F62.9 absence-style tests). Pending
// scaffold; /build-loop (PLAN T27) implements and removes Skip.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDisclosureContractCompleteness
{
    private const string Pending = "pending — PLAN T27 (/build-loop)";

    public static class ScenarioExactShapesPinned
    {
        [Fact(Skip = Pending)]
        public static void Every_public_dto_matches_its_specced_shape_exactly()
        {
            // Given every spectator DTO type
            // When  the contract test reflects its serialized property names
            // Then  each matches the spec'd shape exactly, including listeners on
            //       now-playing per amended F62.4 (F67.6)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathUnblessedField
    {
        [Fact(Skip = Pending)]
        public static void Extra_property_fails_naming_the_unexpected_member()
        {
            // Given a test DTO derived from a public DTO with one extra property
            // When  the contract assertion runs against it
            // Then  it fails naming the unexpected property (F67.6)
            Assert.Fail(Pending);
        }
    }
}
