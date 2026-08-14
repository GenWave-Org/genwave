// STORY-247 — Two-stage firing with a parachute: benching by unpainting, the repository half
// (SPEC F91.9, PLAN T121)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (mirrors Story240_ScheduleStore.cs's
// own fixture family: ScheduleTestPersonas.InsertAsync, ScheduleRepository/PersonaRepository over
// StationDataSource). STORY-247's own Given/When narrative names "a slot removed via PUT
// /api/schedule" — that wire endpoint is T122's own task, not yet built, and GenWave.Host.Tests has
// no Postgres fixture to verify "no longer appears in any schedule row" against even if it could
// (Story251_ExplicitOverrideEndpoint.cs's own remarks document that Host/MediaLibrary split) — so
// Story247_TwoStageFiring.cs (Host.Tests) leaves ScenarioBenchingByUnpainting pending, tagged T122.
//
// This file proves the same "benching by unpainting" behavior one layer down, with what already
// exists today: ScheduleRepository.ReplaceWeekAsync (T118, already shipped) submitting a week that no
// longer names the persona for the one slot it used to hold IS "unpainting" that slot — no separate
// "unpaint" verb exists, or needs to. The API-level round trip through GET/PUT /api/schedule re-pins
// once T122 lands.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTwoStageFiringBenchTransition
{
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBenchingByUnpainting(DatabaseFixture db)
    {
        // Given a DJ scheduled in one slot, When that slot is removed by replacing the week without
        // it (ScheduleRepository.ReplaceWeekAsync — the store beneath T122's PUT /api/schedule).

        /// <summary>
        /// PLAN T241 review: mirrors Story240_ScheduleStore.cs's own identically-purposed helper —
        /// see its own remarks in full. <see cref="ScheduleRepository"/>'s load query now LEFT JOINs
        /// <c>station.show</c> keyed on <c>segment_schedule.show_id</c> (SPEC F116.1), so this file
        /// also needs BOTH idempotent migration scripts (db/33 then db/35) re-run before every fact's
        /// own connection, regardless of xUnit's class scheduling against
        /// Story242_UpgradeChangesNothing.cs's and Story305_ShowRepository.cs's own in-place
        /// scenarios.
        /// </summary>
        static ScheduleRepository ScheduleRepo(DatabaseFixture db)
        {
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));
            return new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<ScheduleRepository>.Instance);
        }

        [Fact]
        public async Task PersonaRecordIsUntouched()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Bench Transition DJ");
            var scheduleRepo = ScheduleRepo(db);
            var personaRepo = new PersonaRepository(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
            await scheduleRepo.ReplaceWeekAsync(
                [new ScheduleSegment(null, DayOfWeek.Monday, 0, 600, personaId, Genres: null, EnergyMin: null, EnergyMax: null)],
                expectedVersion: null, CancellationToken.None);
            var before = await personaRepo.GetByIdAsync(personaId, CancellationToken.None);

            // When: the week is replaced again, this time with no slot naming this persona at all.
            await scheduleRepo.ReplaceWeekAsync([], expectedVersion: null, CancellationToken.None);

            var after = await personaRepo.GetByIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        }

        [Fact]
        public async Task PersonaNoLongerAppearsInAnyScheduleRow()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Bench Transition DJ");
            var scheduleRepo = ScheduleRepo(db);
            await scheduleRepo.ReplaceWeekAsync(
                [new ScheduleSegment(null, DayOfWeek.Monday, 0, 600, personaId, Genres: null, EnergyMin: null, EnergyMax: null)],
                expectedVersion: null, CancellationToken.None);

            // When: unpainted — replaced with a week that never names this persona.
            await scheduleRepo.ReplaceWeekAsync([], expectedVersion: null, CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "select count(*)::int from station.segment_schedule where persona_id = @personaId",
                new { personaId });
            Assert.Equal(0, count);
        }
    }
}
