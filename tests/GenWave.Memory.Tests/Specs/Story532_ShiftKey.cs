// STORY-532 — Every DJ line is on the record (gh-#293 · SPEC F218 · PLAN T682)
//
// ShiftKey is a pure value (GenWave.Core.Domain): factory shapes, parse round-trip.

using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureShiftKey
{
    static readonly DateOnly Date = new(2026, 11, 8);

    public sealed class ScenarioAScheduledSegmentHasAShiftKey
    {
        // Given a persona on air in a scheduled segment
        // When  the shift key is built

        [Fact]
        public void ItIsThePersonaIdDayStartMinuteAndStationLocalDate() =>
            Assert.Equal("7|0|1260|2026-11-08", ShiftKey.Scheduled(7, DayOfWeek.Sunday, 1260, Date).ToString());
    }

    public sealed class ScenarioAnUnscheduledHourHasAShiftKey
    {
        // Given no segment is on air
        // When  the shift key is built

        [Fact]
        public void ItIsThePersonaIdAndTheStationLocalDate() =>
            Assert.Equal("7|2026-11-08", ShiftKey.Unscheduled(7, Date).ToString());
    }

    public sealed class ScenarioAKeyRoundTripsThroughText
    {
        // Given a scheduled key's text
        // When  it is parsed

        [Fact]
        public void TheParsedKeyEqualsTheOriginal()
        {
            var original = ShiftKey.Scheduled(7, DayOfWeek.Friday, 90, Date);

            Assert.Equal(original, ShiftKey.TryParse(original.ToString(), out var parsed) ? parsed : null);
        }
    }

    public sealed class ScenarioMalformedTextIsRejected
    {
        // Given text that is not a shift key
        // When  it is parsed

        [Theory]
        [InlineData("")]
        [InlineData("7")]
        [InlineData("7|not-a-date")]
        [InlineData("7|9|0|2026-11-08")]
        [InlineData("7|1|1440|2026-11-08")]
        public void ParsingFails(string text) => Assert.False(ShiftKey.TryParse(text, out _));
    }
}
