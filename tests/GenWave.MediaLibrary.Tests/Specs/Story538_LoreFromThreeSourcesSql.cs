// STORY-538 — Lore from three sources, the SQL half (SPEC F218.20 · PLAN T705)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture). AC1 (the card's Lore
// lands as authored rows) and the "eligible for recall" half of AC4 only hold in SQL, so only a real database pins
// them; the HTTP half (AC2-AC4) is in tests/GenWave.Host.Tests/Specs/Story538_LoreFromThreeSources.cs.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLoreFromThreeSourcesSql
{
    static PersonaMemoryRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions()));

    static readonly MemoryCandidateQuery LoreQuery =
        new(MemoryKinds.Lore, 10, TimeSpan.Zero, MemoryRecallOrder.LeastRecentlyAired);

    // AC1 — The card's Lore imports
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCardSLoreImports(DatabaseFixture db) : IAsyncLifetime
    {
        const string Line = "Lost the 1998 regional DJ final to a coin toss";
        IReadOnlyList<PersonaMemoryAdminRow> rows = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            var card = new PersonaCard(
                SchemaVersion: PersonaCard.CurrentSchemaVersion, Name: "DJ Nova", Tagline: "Late-night gravity.",
                Soul: "A slow-orbit voice.", Quirks: [], Voice: new VoiceSpec("kokoro", "af_heart", 1.0, "en"),
                EnergyDisposition: 0, Lore: [Line], Corrections: []);
            var import = new PersonaImportRepository(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
            var outcome = await import.ImportAsync(new PersonaImportRequest("dj-nova", "", card), CancellationToken.None);
            var imported = Assert.IsType<PersonaImportOutcome.Imported>(outcome);
            rows = await Repo(db).ListForAdminAsync(imported.PersonaId, MemoryKinds.Lore, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePersonaHasAPersonaMemoryRowKindLoreSourceAuthoredWithThatText()
        {
            var row = Assert.Single(rows);
            Assert.Equal(Line, row.Content);
            Assert.Equal(MemoryKinds.Lore, row.Kind);
            Assert.Equal(MemorySources.Authored, row.Source);
        }
    }

    // AC3 + AC4 — drafted rows are pending until approved, and only then recalled
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioApproveMakesItReal(DatabaseFixture db) : IAsyncLifetime
    {
        long persona;
        IReadOnlyList<PersonaMemoryAdminRow> drafted = [];
        IReadOnlyList<string> recalledBefore = [];
        IReadOnlyList<string> recalledAfter = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                persona = await conn.QuerySingleAsync<long>("insert into station.persona (name) values ('Sal') returning id::bigint");
            var repo = Repo(db);
            drafted = await repo.AddDraftedLoreAsync(persona, ["grew up above a bakery", "once met a bear"], CancellationToken.None);
            recalledBefore = (await repo.CandidatesAsync(persona, LoreQuery, CancellationToken.None)).Select(r => r.Content).ToList();
            await repo.UpdateAsync(persona, drafted[0].Id, new PersonaMemoryChange(null, true, null), CancellationToken.None);
            recalledAfter = (await repo.CandidatesAsync(persona, LoreQuery, CancellationToken.None)).Select(r => r.Content).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheDraftedRowsAreLoreSourceDraftedWithNoApprovedAt() =>
            Assert.All(drafted, r =>
            {
                Assert.Equal(MemoryKinds.Lore, r.Kind);
                Assert.Equal(MemorySources.Drafted, r.Source);
                Assert.Null(r.ApprovedAt);
            });

        [Fact]
        public void APendingRowIsNotRecalled() => Assert.Empty(recalledBefore);

        [Fact]
        public void TheRowIsEligibleForRecallOnceApproved() =>
            Assert.Equal(["grew up above a bakery"], recalledAfter);
    }
}
