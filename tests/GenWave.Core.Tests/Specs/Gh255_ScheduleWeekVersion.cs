// gh-#255 — schedule: silent save-loss guard, the fingerprint half.
//
// BDD specification — xUnit, pure domain (no DB, no HTTP). ScheduleWeekVersion.Compute is the
// content fingerprint GET/PUT /api/schedule exchange so a stale editor's full-replace can be
// rejected instead of silently wiping a week saved after that editor loaded. These facts pin the
// properties the guard depends on: content-identical weeks agree regardless of row ids or input
// order (ReplaceWeekAsync reassigns ids on every write — id-sensitivity would 409 an editor whose
// grid still matches the store exactly), and any CONTENT difference — including on a
// whole-week-spanning block's own fields — changes the fingerprint.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureScheduleWeekVersion
{
    static ScheduleSegment Segment(
        DayOfWeek day, int start, int end, long? personaId = null, long? id = null,
        string[]? genres = null, double? energyMin = null, double? energyMax = null) =>
        new(id, day, start, end, personaId, genres, energyMin, energyMax);

    /// <summary>A block spanning every day of the week — the gh-#255 repro shape.</summary>
    static List<ScheduleSegment> FullWeekBand(long personaId, long firstId = 0) =>
        Enumerable.Range(0, 7)
            .Select(day => Segment((DayOfWeek)day, 600, 720, personaId, firstId == 0 ? null : firstId + day))
            .ToList();

    public sealed class ScenarioContentIdenticalWeeksAgree
    {
        [Fact]
        public void TheSameContentProducesTheSameFingerprint()
        {
            Assert.Equal(
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 7)),
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 7)));
        }

        [Fact]
        public void RowIdsNeverInfluenceTheFingerprint()
        {
            // Delete-then-insert reassigns every id on every save — the fingerprint must see the
            // re-saved identical week as the SAME week or every legitimate save would 409 itself.
            Assert.Equal(
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 7, firstId: 1)),
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 7, firstId: 500)));
        }

        [Fact]
        public void InputOrderNeverInfluencesTheFingerprint()
        {
            var week = FullWeekBand(personaId: 7);
            var reversed = week.AsEnumerable().Reverse().ToList();

            Assert.Equal(ScheduleWeekVersion.Compute(week), ScheduleWeekVersion.Compute(reversed));
        }

        [Fact]
        public void AnEmptyWeekHasAStableFingerprintOfItsOwn()
        {
            Assert.Equal(ScheduleWeekVersion.Compute([]), ScheduleWeekVersion.Compute([]));
            Assert.NotEqual(ScheduleWeekVersion.Compute([]), ScheduleWeekVersion.Compute(FullWeekBand(7)));
        }
    }

    public sealed class ScenarioContentChangesChangeTheFingerprint
    {
        [Fact]
        public void ChangingOneDayOfAFullWeekBandChangesTheFingerprint()
        {
            var band = FullWeekBand(personaId: 7);
            var shifted = FullWeekBand(personaId: 7);
            shifted[6] = shifted[6] with { StartMinute = 630 };

            Assert.NotEqual(ScheduleWeekVersion.Compute(band), ScheduleWeekVersion.Compute(shifted));
        }

        [Fact]
        public void ChangingThePersonaChangesTheFingerprint()
        {
            Assert.NotEqual(
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 7)),
                ScheduleWeekVersion.Compute(FullWeekBand(personaId: 8)));
        }

        [Fact]
        public void ChangingOnlyAnEnvelopeOverrideChangesTheFingerprint()
        {
            var plain = new[] { Segment(DayOfWeek.Monday, 0, 1440) };
            var enveloped = new[] { Segment(DayOfWeek.Monday, 0, 1440, genres: ["jazz"], energyMin: 0.2, energyMax: 0.9) };

            Assert.NotEqual(ScheduleWeekVersion.Compute(plain), ScheduleWeekVersion.Compute(enveloped));
        }

        [Fact]
        public void DroppingOneSegmentOfAFullWeekBandChangesTheFingerprint()
        {
            var full = FullWeekBand(personaId: 7);
            var missingSunday = full.Where(s => s.Day != DayOfWeek.Sunday).ToList();

            Assert.NotEqual(ScheduleWeekVersion.Compute(full), ScheduleWeekVersion.Compute(missingSunday));
        }
    }
}
