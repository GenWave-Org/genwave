// STORY-540 — Memory admin SQL (SPEC F218.19/.20/.24 · PLAN T706)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture). The persona-and-row
// scoping, the pending flag source and the approve/forget stamps run in SQL, so only a real database pins them.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMemoryAdminSql
{
    static PersonaMemoryRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions()));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }

    static async Task<long> PlantAsync(DatabaseFixture db, long personaId, string kind, string source, string content)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona_memory (persona_id, kind, content, source) values (@personaId, @kind, @content, @source) returning id::bigint",
            new { personaId, kind, content, source });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOperatorShapesOnePersonasRows(DatabaseFixture db) : IAsyncLifetime
    {
        long sal;
        long other;
        long theirs;
        long drafted;
        long life;
        IReadOnlyList<PersonaMemoryAdminRow> listed = [];
        PersonaMemoryAdminRow added = new(0, "", "", "", 0, null, DateTime.MinValue, null, null);

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            sal = await CreatePersonaAsync(db, "Sal");
            other = await CreatePersonaAsync(db, "Other");
            theirs = await PlantAsync(db, other, "lore", "authored", "not Sal's");
            drafted = await PlantAsync(db, sal, "lore", "drafted", "once met a bear");
            life = await PlantAsync(db, sal, "life", "extracted", "has a beagle named Gus");
            await PlantAsync(db, sal, "quirk", "accrued", "spent quirk");
            var repo = Repo(db);
            added = await repo.AddAuthoredLoreAsync(sal, "grew up above a bakery", CancellationToken.None);
            listed = await repo.ListForAdminAsync(sal, null, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListHoldsOnlyThisPersonasLoreEpisodeAndLifeRows() =>
            Assert.Equal(
                new[] { "grew up above a bakery", "has a beagle named Gus", "once met a bear" },
                listed.Select(r => r.Content).Order().ToArray());

        [Fact]
        public void AddedLoreIsAuthoredAndNotPending()
        {
            Assert.Equal("authored", added.Source);
            Assert.Equal("lore", added.Kind);
            Assert.False(added.Pending);
        }

        [Fact]
        public void ADraftedRowIsPendingUntilApproved() =>
            Assert.True(listed.Single(r => r.Id == drafted).Pending);

        [Fact]
        public async Task ApprovingStampsApprovedAtAndClearsPending()
        {
            var repo = Repo(db);

            Assert.Equal(PersonaMemoryUpdateStatus.Updated, (await repo.UpdateAsync(sal, drafted, new PersonaMemoryChange(null, true, null), CancellationToken.None)).Status);

            var row = (await repo.ListForAdminAsync(sal, "lore", CancellationToken.None)).Single(r => r.Id == drafted);
            Assert.NotNull(row.ApprovedAt);
            Assert.False(row.Pending);
        }

        [Fact]
        public async Task ForgettingStampsForgottenAtAndUnforgettingClearsIt()
        {
            var repo = Repo(db);

            Assert.Equal(PersonaMemoryUpdateStatus.Updated, (await repo.UpdateAsync(sal, life, new PersonaMemoryChange(null, false, true), CancellationToken.None)).Status);
            Assert.True((await repo.ListForAdminAsync(sal, "life", CancellationToken.None)).Single().Forgotten);

            Assert.Equal(PersonaMemoryUpdateStatus.Updated, (await repo.UpdateAsync(sal, life, new PersonaMemoryChange(null, false, false), CancellationToken.None)).Status);
            Assert.False((await repo.ListForAdminAsync(sal, "life", CancellationToken.None)).Single().Forgotten);
        }

        [Fact]
        public async Task EditingChangesTheText()
        {
            var repo = Repo(db);

            Assert.Equal(PersonaMemoryUpdateStatus.Updated, (await repo.UpdateAsync(sal, life, new PersonaMemoryChange("has a beagle named Rex", false, null), CancellationToken.None)).Status);

            Assert.Equal("has a beagle named Rex", (await repo.ListForAdminAsync(sal, "life", CancellationToken.None)).Single().Content);
        }

        [Fact]
        public async Task AnotherPersonasRowCannotBeTouchedThroughThisOne()
        {
            var repo = Repo(db);

            var change = new PersonaMemoryChange("stolen", true, true);
            Assert.Equal(PersonaMemoryUpdateStatus.NotFound, (await repo.UpdateAsync(sal, theirs, change, CancellationToken.None)).Status);
            Assert.False(await repo.DeleteAsync(sal, theirs, CancellationToken.None));
            Assert.Equal("not Sal's", Assert.Single(await repo.ListForAdminAsync(other, null, CancellationToken.None)).Content);
        }

        [Fact]
        public async Task ApprovingANonDraftedRowChangesNothing()
        {
            var repo = Repo(db);

            var result = await repo.UpdateAsync(sal, life, new PersonaMemoryChange("rewritten", true, true), CancellationToken.None);

            Assert.Equal(PersonaMemoryUpdateStatus.NotDrafted, result.Status);
            var row = (await repo.ListForAdminAsync(sal, "life", CancellationToken.None)).Single();
            Assert.Equal("has a beagle named Gus", row.Content);
            Assert.Null(row.ApprovedAt);
            Assert.False(row.Forgotten);
        }

        [Fact]
        public async Task OneUpdateAppliesTextApprovalAndForgettingTogetherAndReturnsTheRow()
        {
            var repo = Repo(db);

            var result = await repo.UpdateAsync(sal, drafted, new PersonaMemoryChange("met a moose", true, true), CancellationToken.None);

            Assert.Equal(PersonaMemoryUpdateStatus.Updated, result.Status);
            Assert.Equal("met a moose", result.Row?.Content);
            Assert.NotNull(result.Row?.ApprovedAt);
            Assert.True(result.Row?.Forgotten);
        }

        [Fact]
        public async Task DeletingRemovesTheRow()
        {
            var repo = Repo(db);

            Assert.True(await repo.DeleteAsync(sal, drafted, CancellationToken.None));
            Assert.False(await repo.DeleteAsync(sal, drafted, CancellationToken.None));
            Assert.DoesNotContain(await repo.ListForAdminAsync(sal, null, CancellationToken.None), r => r.Id == drafted);
        }
    }
}
