// STORY-306 — One identity chokepoint (F115.2, F116.1)
//
// BDD specification — xUnit, real GenWave.Orchestration.ScheduleResolver/EffectiveAssignment (no
// stores needed: every fact here builds a ScheduleWeekSnapshot by hand, mirroring
// Story241_StationFollowsTheClock.cs's own ScenarioResolvingTheCurrentSegment idiom — a pure
// (snapshot, wall clock) function needs no IScheduleStore double at all). Orchestration.Tests carries
// no live Postgres (fakes only — see project header); the live-PG half of the dormant-columns-unread
// pin (SPEC F115.2 — hand-populating station.show.persona_id/envelope through raw SQL and proving the
// loaded ScheduleWeekSnapshot is unaffected) lives in GenWave.MediaLibrary.Tests'
// Story240_ScheduleStore.cs instead, where it can be real.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureIdentityChokepoint
{
    static readonly ShowSummary NightMoves = new(1, "Night Moves", "Late-night deep cuts", "moody, sparse, past midnight");

    public sealed class ScenarioSnapshotCarriesTheShow
    {
        // Given a block assigned show "Night Moves".

        [Fact]
        public void ShowRidesTheSnapshotDuringItsBlock()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var block = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: null, EnergyMin: null, EnergyMax: null, Show: NightMoves);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            // When the resolver snapshot is read during that block
            var result = resolver.Resolve(new ScheduleWeekSnapshot([block]));

            // Then show id/name/tagline/flavor ride OnAirSnapshot — both at the top-level convenience
            // member (mirrors PersonaId's own "no null-check Segment first" rationale) and on the
            // resolved Segment itself, since ScheduleResolver never rebuilds/clones the block.
            Assert.Equal(NightMoves, result.Show);
            Assert.Equal(NightMoves, result.Segment!.Show);
        }

        [Fact]
        public void UnnamedBlocksCarryNullEndToEnd()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;

            // Given a block with no show (the default — every pre-T241 construction site's own shape)
            var unnamedBlock = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            // When the snapshot is read during that block
            var staffedResult = resolver.Resolve(new ScheduleWeekSnapshot([unnamedBlock]));

            // Then the show member is null end to end: the snapshot's own member, the resolved
            // Segment, and NextSegment (a second unnamed block immediately following) all agree.
            var nextUnnamedBlock = new ScheduleSegment(
                Id: 2, Day: day, StartMinute: 720, EndMinute: 900,
                PersonaId: 9, Genres: null, EnergyMin: null, EnergyMax: null);
            var withNext = resolver.Resolve(new ScheduleWeekSnapshot([unnamedBlock, nextUnnamedBlock]));

            Assert.Null(staffedResult.Show);
            Assert.Null(staffedResult.Segment!.Show);
            Assert.Null(withNext.NextSegment!.Show);

            // ...and a grid gap (no block on air at all) reports the same honest null.
            var gapResult = resolver.Resolve(new ScheduleWeekSnapshot([]));
            Assert.Null(gapResult.Show);
        }
    }

    public sealed class ScenarioDormantMeansDormant
    {
        [Fact]
        public void HandPopulatedBundleColumnsChangeNothing()
        {
            // Given station.show rows with persona_id/envelope hand-populated (F115.2's pin). This
            // project carries no live Postgres (see file header) — ShowSummary itself structurally
            // enforces the pin (no PersonaId/Envelope member exists to even hand-populate at the C#
            // level; the live-PG half proving a REAL station.show row survives this unread lives in
            // GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs). What THIS fact pins is the
            // resolver-side half of the same law: EffectiveAssignment.Resolve/ScheduleResolver never
            // let a show's presence — named, tagline'd, flavor'd, whatever it carries — influence which
            // persona resolves. Two otherwise-identical blocks, differing ONLY in PersonaId, carry the
            // exact SAME show; the show identity is proven fully independent of persona resolution.
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var staffedBlock = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: null, EnergyMin: null, EnergyMax: null, Show: NightMoves);
            var musicOnlyBlock = staffedBlock with { PersonaId = null };
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            // When any v1 path resolves identity
            var staffedResult = resolver.Resolve(new ScheduleWeekSnapshot([staffedBlock]));
            var musicOnlyResult = resolver.Resolve(new ScheduleWeekSnapshot([musicOnlyBlock]));

            // Then behavior is UNCHANGED — block-level persona only, and the SAME show identity rides
            // both snapshots regardless of which persona (if any) the block itself names.
            Assert.Equal(7L, staffedResult.PersonaId);
            Assert.Null(musicOnlyResult.PersonaId);
            Assert.Equal(NightMoves, staffedResult.Show);
            Assert.Equal(NightMoves, musicOnlyResult.Show);
        }
    }

    public sealed class ScenarioShowlessStationsAreUntouched
    {
        [Fact]
        public void ShowlessSnapshotIsByteIdentical()
        {
            // Given a station with zero shows — a two-segment week built the exact same way
            // Story241_StationFollowsTheClock.cs's own SnapshotCarriesSegmentPersonaEnvelopeBoundaryAndNext
            // fact does, with no Show argument passed anywhere (the pre-F116 construction shape,
            // verbatim).
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;

            var onAir = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: ["Rock"], EnergyMin: 0.2, EnergyMax: 0.8);
            var upNext = new ScheduleSegment(
                Id: 2, Day: day, StartMinute: 720, EndMinute: 900,
                PersonaId: 9, Genres: null, EnergyMin: null, EnergyMax: null);
            var snapshot = new ScheduleWeekSnapshot([onAir, upNext]);
            var stationDefault = new FakeStationDefaultEnvelopeSource(
                new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Jazz"], new EnergyRange(0.0, 1.0)));
            var resolver = new ScheduleResolver(time, stationDefault);

            // When the resolver produces snapshots across a full week (the boundary crossing below
            // stands in for "across the week" — the SAME assertions Story241's own pre-F116 fact
            // makes, at the SAME instant, the honest way to compare "byte-identical to pre-F116
            // behavior" without duplicating that file's own full DST/boundary coverage here)
            var result = resolver.Resolve(snapshot);

            // Then output matches pre-F116 behavior exactly — every field Story241's own fact already
            // pins, unchanged (compared field-by-field for Envelope.Genres, same reference-equality
            // caveat that file's own fact documents)...
            Assert.Equal(onAir, result.Segment);
            Assert.Equal(7L, result.PersonaId);
            Assert.Equal(new TimeOnly(9, 0), result.Envelope.StartsAt);
            Assert.Equal(new TimeOnly(12, 0), result.Envelope.EndsAt);
            Assert.Equal(["Rock"], result.Envelope.Genres);
            Assert.Equal(new EnergyRange(0.2, 0.8), result.Envelope.EnergyRange);
            Assert.Equal(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Equal(upNext, result.NextSegment);

            // ...plus the ONE additive member this epic introduces, honestly null (the epic's null
            // hypothesis: a showless station gains a field that is always empty, nothing else).
            Assert.Null(result.Show);
        }
    }
}
