// gh-#255 — schedule: silent save-loss guard, the store half.
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection, same
// harness as Story240_ScheduleStore.cs. Proves ScheduleRepository's expectedVersion guard against
// the REAL table: a whole-week block (the issue's repro shape) replaces and round-trips; a replace
// carrying a stale fingerprint is rejected as VersionConflict and writes NOTHING — the demo-box
// telemetry showed a stale editor's full-replace destroying six just-saved segments with no error
// anywhere (Loki 2026-07-28 12:59:47 → 13:00:30, segmentCount 54 → 48); this guard is what makes
// that impossible. expectedVersion: null keeps every legacy caller's behavior byte-identical.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScheduleVersionGuard
{
    /// <summary>
    /// PLAN T241 review: mirrors Story240_ScheduleStore.cs's own identically-named helper — see its
    /// own remarks in full. <see cref="ScheduleRepository"/>'s load query now LEFT JOINs
    /// <c>station.show</c> keyed on <c>segment_schedule.show_id</c> (SPEC F116.1), so this file also
    /// needs BOTH idempotent migration scripts (db/33 then db/35) re-run before every fact's own
    /// connection, regardless of xUnit's class scheduling against Story242_UpgradeChangesNothing.cs's
    /// and Story305_ShowRepository.cs's own in-place scenarios.
    /// </summary>
    static ScheduleRepository Repo(DatabaseFixture db)
    {
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));
        return new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
    }

    static ScheduleSegment MusicOnly(DayOfWeek day, int start, int end) =>
        new(null, day, start, end, PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);

    /// <summary>The gh-#255 repro: one 2h block, every day of the week.</summary>
    static List<ScheduleSegment> FullWeekBand() =>
        Enumerable.Range(0, 7).Select(day => MusicOnly((DayOfWeek)day, 600, 720)).ToList();

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAWholeWeekBlockRoundTrips(DatabaseFixture db)
    {
        [Fact]
        public async Task ABandAcrossAllSevenDaysReplacesAndLoadsBackIdentically()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync(FullWeekBand(), expectedVersion: null, CancellationToken.None);

            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            Assert.Equal(7, replaced.Snapshot.Segments.Count);

            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(7, loaded.Segments.Count);
            for (var day = 0; day < 7; day++)
            {
                var segment = Assert.Single(loaded.Segments, s => s.Day == (DayOfWeek)day);
                Assert.Equal(600, segment.StartMinute);
                Assert.Equal(720, segment.EndMinute);
            }
        }

        [Fact]
        public async Task SevenFullDayRowsReplaceAndLoadBackIdentically()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var fullWeek = Enumerable.Range(0, 7)
                .Select(day => MusicOnly((DayOfWeek)day, 0, 1440))
                .ToList();

            var result = await repo.ReplaceWeekAsync(fullWeek, expectedVersion: null, CancellationToken.None);

            Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(7, loaded.Segments.Count);
            Assert.All(loaded.Segments, s =>
            {
                Assert.Equal(0, s.StartMinute);
                Assert.Equal(1440, s.EndMinute);
            });
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAStaleReplaceIsRejected(DatabaseFixture db)
    {
        [Fact]
        public async Task AMismatchedExpectedVersionReturnsVersionConflictAndWritesNothing()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            // The stored week moves on (another tab's save)…
            await repo.ReplaceWeekAsync(FullWeekBand(), expectedVersion: null, CancellationToken.None);
            // …while this editor still holds the EMPTY week's fingerprint from before it.
            var staleVersion = ScheduleWeekVersion.Compute([]);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Monday, 0, 600)], staleVersion, CancellationToken.None);

            var conflict = Assert.IsType<ScheduleReplaceResult.VersionConflict>(result);
            var current = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(7, current.Segments.Count); // the newer save survives untouched.
            Assert.Equal(ScheduleWeekVersion.Compute(current.Segments), conflict.CurrentVersion);
        }

        [Fact]
        public async Task AMatchingExpectedVersionReplacesNormally()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await repo.ReplaceWeekAsync(FullWeekBand(), expectedVersion: null, CancellationToken.None);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Monday, 0, 600)],
                ScheduleWeekVersion.Compute(loaded.Segments),
                CancellationToken.None);

            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            Assert.Single(replaced.Snapshot.Segments);
        }

        [Fact]
        public async Task ValidationStillRunsBeforeTheVersionGuardEverMatters()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Monday, 15, 1440)], // off-grid start minute
                ScheduleWeekVersion.Compute([]),
                CancellationToken.None);

            Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
        }
    }
}
