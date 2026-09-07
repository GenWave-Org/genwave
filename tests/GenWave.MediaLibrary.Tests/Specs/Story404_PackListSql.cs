// STORY-404 — the attributions endpoint's own pack listing SQL (SPEC F169.2 · PLAN T419)
//
// Two halves, mirroring Story401_VoicePackDeleteGuardSql.cs's own split one repository over: a pure
// text pin of JinglePackRepository.ListSql/VoicePackRepository.ListSql (no Postgres needed — a
// mutation dropping the `order by slug` clause has nowhere else to hide), plus one real-Postgres
// round trip per store proving ListAsync actually comes back ordered by slug against a real table,
// not merely that the SQL text LOOKS right.

using Dapper;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePackListSql
{
    // ---------------------------------------------------------------------
    // Pure text pin — no Postgres needed
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheListingQueriesAreOrderedBySlug
    {
        [Fact]
        public void JinglePackListSqlSelectsFromStationJinglePackOrderedBySlug()
        {
            Assert.Contains("from station.jingle_pack", JinglePackRepository.ListSql, StringComparison.Ordinal);
            Assert.Contains("order by slug", JinglePackRepository.ListSql, StringComparison.Ordinal);
        }

        [Fact]
        public void VoicePackListSqlSelectsFromStationVoicePackOrderedBySlug()
        {
            Assert.Contains("from station.voice_pack", VoicePackRepository.ListSql, StringComparison.Ordinal);
            Assert.Contains("order by slug", VoicePackRepository.ListSql, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // Real Postgres — two rows in, two back, ordered
    // ---------------------------------------------------------------------

    static JinglePackRepository JingleRepo(DatabaseFixture db) =>
        new(
            new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            new Lazy<NpgsqlDataSource>(() => db.DataSource),
            NullLogger<JinglePackRepository>.Instance);

    static VoicePackRepository VoiceRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<VoicePackRepository>.Instance);

    /// <summary>DatabaseFixture carries no <c>ResetVoicePackAsync</c> (that file is outside this
    /// task's owned partition) — the same inline raw-SQL reset every other Scenario-local helper in
    /// this project uses when the shared fixture doesn't already carry one. <c>cascade</c>: db/45's
    /// own <c>voice_pack_voice.pack_id references station.voice_pack(id) on delete cascade</c> only
    /// governs DELETE, not TRUNCATE — a row left behind by an earlier test elsewhere in this shared
    /// database would otherwise refuse the truncate outright.</summary>
    static async Task ResetVoicePackAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate table station.voice_pack restart identity cascade");
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioJinglePackListComesBackOrderedBySlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TwoInstalledPacksComeBackAsTwoRowsOrderedBySlug()
        {
            await db.ResetJinglePackAsync();
            var repo = JingleRepo(db);

            await repo.UpsertAsync("z-pack", """{"packName":"Z Pack"}""", "test-fixture", [], CancellationToken.None);
            await repo.UpsertAsync("a-pack", """{"packName":"A Pack"}""", "test-fixture", [], CancellationToken.None);

            var rows = await repo.ListAsync(CancellationToken.None);

            Assert.Equal(2, rows.Count);
            Assert.Equal(["a-pack", "z-pack"], rows.Select(r => r.Slug).ToArray());
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVoicePackListComesBackOrderedBySlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TwoInstalledPacksComeBackAsTwoRowsOrderedBySlug()
        {
            await ResetVoicePackAsync(db);
            var repo = VoiceRepo(db);

            await repo.UpsertAsync("z-voices", "kokoro", """{"packName":"Z Voices"}""", "test-fixture", [], CancellationToken.None);
            await repo.UpsertAsync("a-voices", "kokoro", """{"packName":"A Voices"}""", "test-fixture", [], CancellationToken.None);

            var rows = await repo.ListAsync(CancellationToken.None);

            Assert.Equal(2, rows.Count);
            Assert.Equal(["a-voices", "z-voices"], rows.Select(r => r.Slug).ToArray());
        }
    }
}
