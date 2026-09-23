// STORY-469 — Aired means recorded once (gh-#773 · SPEC F202 · PLAN T554)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (mirrors
// Story357_AnnouncementStore.cs's own ScenarioMarkAiredReturnsTheCollapseCount fixture shape: direct
// AnnouncementRepository construction over StationDataSource, an independent raw-SQL read for
// verifying the write rather than reading back through the repository under test). AC2 — a Postgres
// fact on MarkAiredAsync's own idempotent WHERE (SPEC F202.3) — moved here from
// Story469_AiredMeansRecordedOnce.cs (Host.Tests has no DatabaseFixture).

using Dapper;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAiredMeansRecordedOnce
{
    /// <summary>An independent raw-SQL read (bypasses <see cref="AnnouncementRepository"/> itself) so
    /// a fact verifies what the repository under test actually persisted — mirrors
    /// <c>Story357_AnnouncementStore.ReadRowAsync</c>'s own posture, narrowed to the one column this
    /// scenario cares about. <see cref="DateTime"/>, not <see cref="DateTimeOffset"/> — matches
    /// <see cref="AnnouncementRow.AiredAt"/>'s own column type exactly; no Dapper
    /// <see cref="DateTimeOffset"/> type handler is registered anywhere in this assembly, so Npgsql's
    /// own <c>timestamptz</c>-&gt;<see cref="DateTime"/> value would otherwise fail to convert on the
    /// scalar read.</summary>
    static async Task<DateTime?> ReadAiredAtAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<DateTime?>(
            "select aired_at from station.announcement where id = @id", new { id });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnAlreadyAiredRow(DatabaseFixture db)
    {
        [Fact]
        public async Task ARepeatedMarkAiredLeavesAiredAtUnchanged()
        {
            // Given a claimed announcement that has already aired once...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "The garage sale starts at nine", verbatim: false, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            var id = Assert.Single(claimed).Id;
            await repo.MarkAiredAsync(id, CancellationToken.None);
            var first = await ReadAiredAtAsync(db, id);

            // When MarkAiredAsync runs again for the SAME id (a duplicate signal — SPEC F202.3)...
            await repo.MarkAiredAsync(id, CancellationToken.None);
            var second = await ReadAiredAtAsync(db, id);

            // Then aired_at is unchanged — the idempotent WHERE made the second call a no-op.
            Assert.Equal(first, second);
        }

        [Fact]
        public async Task ARepeatedMarkAiredReturnsNull()
        {
            // Given a claimed announcement that has already aired once...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            var id = Assert.Single(claimed).Id;
            await repo.MarkAiredAsync(id, CancellationToken.None);

            // When MarkAiredAsync runs again for the SAME id...
            var second = await repo.MarkAiredAsync(id, CancellationToken.None);

            // Then it reports the total transition's own "nothing matched" outcome — never a fault,
            // never a second collapse-count.
            Assert.Null(second);
        }
    }
}
