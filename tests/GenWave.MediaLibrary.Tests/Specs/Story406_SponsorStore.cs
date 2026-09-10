// STORY-406/STORY-407/STORY-410 — SponsorRepository against real Postgres (SPEC F171 · PLAN T432)
//
// The Story389_AdSpotLifecycleStore precedent, one store over: every scenario runs against the
// DatabaseFixture's own throwaway Postgres, never a mock, because the behaviour under test IS the
// SQL (the fold-collision unique constraint, the reference-count subqueries, the xmin version guard).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSponsorStore
{
    /// <summary>Round-3 finding R1 — <c>UpsertPackSponsorsAsync</c> is a BATCH call now
    /// (<see cref="ISponsorStore.UpsertPackSponsorsAsync"/>'s own contract), so a scenario that only
    /// wants ONE pack sponsor calls it with a one-element list and unwraps the single row back out —
    /// the shared shape every such call site in this file now uses, rather than repeating the
    /// unwrap.</summary>
    static async Task<Sponsor> UpsertOnePackSponsorAsync(
        SponsorRepository repo, string packSlug, string name, CancellationToken ct)
    {
        var result = await repo.UpsertPackSponsorsAsync(packSlug, [name], ct);
        return Assert.IsType<SponsorPackWriteResult.Ok>(result).Sponsors[0];
    }

    // ---------------------------------------------------------------------
    // STORY-406 AC2 — a case/whitespace variant of an existing name collides (SPEC F171.2's own
    // sponsor_fold: lowercase + collapse internal whitespace + trim)
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCreateOwnerAsyncFoldsCaseAndWhitespaceToOneSponsor(DatabaseFixture db)
    {
        static NewSponsor Owner(string name) =>
            new(name, Tagline: null, About: null, Phone: null, Address: null, Website: null, Tone: null);

        [Fact]
        public async Task AFirstCreateLandsAnOkRow()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.CreateOwnerAsync(Owner("Northside Bakery"), CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(result);
            Assert.Equal("Northside Bakery", ok.Sponsor.Name);
            Assert.Null(ok.Sponsor.PackSlug);
        }

        [Fact]
        public async Task ASecondCreateWithDifferentCaseAndSpacingIsRefusedAsNameTaken()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            await repo.CreateOwnerAsync(Owner("Northside Bakery"), CancellationToken.None);

            // "northside  bakery" folds to the same name_key: lowercased, double space collapsed.
            var second = await repo.CreateOwnerAsync(Owner("northside  bakery"), CancellationToken.None);

            Assert.IsType<SponsorWriteResult.NameTaken>(second);
        }
    }

    // ---------------------------------------------------------------------
    // STORY-406 AC3 — the pack namespace and the owner namespace are separate (sponsor_pack_slug_name_key
    // is UNIQUE NULLS NOT DISTINCT (pack_slug, name_key), so pack_slug=NULL and pack_slug='x' never collide)
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePackAndOwnerNamespacesAreSeparate(DatabaseFixture db)
    {
        [Fact]
        public async Task AnOwnerCreateForANameAlreadyPackOwnedSucceedsAndBothCoexist()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var pack = await UpsertOnePackSponsorAsync(repo, "genwave-catalog", "Al's Diner", CancellationToken.None);

            var result = await repo.CreateOwnerAsync(
                new NewSponsor("Al's Diner", Tagline: null, About: null, Phone: null, Address: null, Website: null, Tone: null),
                CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(result);
            Assert.NotEqual(pack.Id, ok.Sponsor.Id);
            Assert.Null(ok.Sponsor.PackSlug);
            Assert.Equal("genwave-catalog", pack.PackSlug);

            var all = await repo.ListAsync(q: null, CancellationToken.None);
            Assert.Equal(2, all.Count(row => row.Sponsor.Name == "Al's Diner"));
        }
    }

    // ---------------------------------------------------------------------
    // Round-3 finding R5 — db/46's own CHECK constraints on the optional profile fields surface as
    // SponsorWriteResult.InvalidField, naming the exact field that failed
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCreateOwnerAsyncRefusesFieldsViolatingChecks(DatabaseFixture db)
    {
        [Fact]
        public async Task ATaglineOverTheLengthCapIsRefusedNamingTagline()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var overlong = new string('a', 161); // db/46: tagline CHECK caps at 160

            var result = await repo.CreateOwnerAsync(
                new NewSponsor(
                    "Bramble & Fitch", Tagline: overlong, About: null, Phone: null, Address: null,
                    Website: null, Tone: null),
                CancellationToken.None);

            var invalid = Assert.IsType<SponsorWriteResult.InvalidField>(result);
            Assert.Equal("tagline", invalid.FieldName);
        }

        [Fact]
        public async Task AWebsiteNotShapedAsHttpOrHttpsIsRefusedNamingWebsite()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.CreateOwnerAsync(
                new NewSponsor(
                    "Bramble & Fitch", Tagline: null, About: null, Phone: null, Address: null,
                    Website: "ftp://example.com", Tone: null), // db/46: website CHECK requires http(s)://
                CancellationToken.None);

            var invalid = Assert.IsType<SponsorWriteResult.InvalidField>(result);
            Assert.Equal("website", invalid.FieldName);
        }
    }

    // ---------------------------------------------------------------------
    // Round-3 finding R1/R5 — UpsertPackSponsorsAsync's own batch contract: idempotent per (packSlug,
    // folded name), refuses the WHOLE batch on a fold collision before any row is written, and surfaces
    // a db/46 CHECK violation the same way CreateOwnerAsync does
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertPackSponsorsAsyncIsBatchAwareAndIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task ABatchOfDistinctBrandsCreatesOneSponsorEachInTheSameOrder()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.UpsertPackSponsorsAsync(
                "genwave-catalog", ["Al's Diner", "Bramble & Fitch"], CancellationToken.None);

            var ok = Assert.IsType<SponsorPackWriteResult.Ok>(result);
            Assert.Equal(2, ok.Sponsors.Count);
            Assert.Equal("Al's Diner", ok.Sponsors[0].Name);
            Assert.Equal("Bramble & Fitch", ok.Sponsors[1].Name);
            Assert.All(ok.Sponsors, s => Assert.Equal("genwave-catalog", s.PackSlug));
        }

        [Fact]
        public async Task ARepeatCallWithTheSameNamesReturnsTheSameSponsorIdsNotDuplicates()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var first = Assert.IsType<SponsorPackWriteResult.Ok>(
                await repo.UpsertPackSponsorsAsync("genwave-catalog", ["Al's Diner"], CancellationToken.None));

            var second = Assert.IsType<SponsorPackWriteResult.Ok>(
                await repo.UpsertPackSponsorsAsync("genwave-catalog", ["Al's Diner"], CancellationToken.None));

            Assert.Equal(first.Sponsors[0].Id, second.Sponsors[0].Id);
            var all = await repo.ListAsync(q: null, CancellationToken.None);
            Assert.Single(all, row => row.Sponsor.Name == "Al's Diner");
        }

        [Fact]
        public async Task TwoDifferentNamesFoldingTheSameRefuseTheWholeBatch()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.UpsertPackSponsorsAsync(
                "genwave-catalog", ["Acme", "ACME "], CancellationToken.None);

            var collide = Assert.IsType<SponsorPackWriteResult.NamesCollide>(result);
            Assert.Equal("Acme", collide.First);
            Assert.Equal("ACME ", collide.Second);
            // Refused before any write — no row lands for either name, not even the earlier one.
            var all = await repo.ListAsync(q: null, CancellationToken.None);
            Assert.Empty(all);
        }

        [Fact]
        public async Task ANameOverTheLengthCapRefusesAsInvalidFieldNamingName()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var overlong = new string('a', 121); // db/46: sponsor.name CHECK caps at 120

            var result = await repo.UpsertPackSponsorsAsync("genwave-catalog", [overlong], CancellationToken.None);

            var invalid = Assert.IsType<SponsorPackWriteResult.InvalidField>(result);
            Assert.Equal("name", invalid.FieldName);
        }
    }

    // ---------------------------------------------------------------------
    // Round-3 finding R3/R5 — FindOrCreateOwnerAsync: an exact fold-match returns the EXISTING row
    // (never a second one), a miss creates
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFindOrCreateOwnerAsyncFindsOrCreates(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstCallForAnUnknownNameCreatesANewOwnerSponsor()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.FindOrCreateOwnerAsync("Bramble & Fitch", CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(result);
            Assert.Equal("Bramble & Fitch", ok.Sponsor.Name);
            Assert.Null(ok.Sponsor.PackSlug);
        }

        [Fact]
        public async Task ASecondCallWithAFoldEqualNameReturnsTheExistingRowNotADuplicate()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var first = Assert.IsType<SponsorWriteResult.Ok>(
                await repo.FindOrCreateOwnerAsync("Bramble & Fitch", CancellationToken.None));

            // "bramble  & fitch" folds to the same name_key: lowercased, double space collapsed.
            var second = await repo.FindOrCreateOwnerAsync("bramble  & fitch", CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(second);
            Assert.Equal(first.Sponsor.Id, ok.Sponsor.Id);
            var all = await repo.ListAsync(q: null, CancellationToken.None);
            Assert.Single(all, row => row.Sponsor.Id == first.Sponsor.Id);
        }

        [Fact]
        public async Task AFoldEqualNameToAnExistingPackSponsorStillCreatesASeparateOwnerRow()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var pack = await UpsertOnePackSponsorAsync(repo, "genwave-catalog", "Al's Diner", CancellationToken.None);

            var result = await repo.FindOrCreateOwnerAsync("Al's Diner", CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(result);
            Assert.NotEqual(pack.Id, ok.Sponsor.Id);
            Assert.Null(ok.Sponsor.PackSlug);
        }
    }

    // ---------------------------------------------------------------------
    // STORY-407 — ListAsync carries briefs/spots(by state)/shows referencing counts alongside each row
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListAsyncCarriesReferencingCounts(DatabaseFixture db)
    {
        static NewAdSpot Spot(long sponsorId, string title, AdState state) =>
            new(
                sponsorId, title, Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
                PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null, InitialState: state,
                FailReason: null);

        [Fact]
        public async Task ANewSponsorListsWithZeroCounts()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            var rows = await repo.ListAsync(q: null, CancellationToken.None);

            var row = Assert.Single(rows, r => r.Sponsor.Id == sponsorId);
            Assert.Equal(0, row.Briefs);
            Assert.Empty(row.SpotsByState);
            Assert.Equal(0, row.Shows);
        }

        [Fact]
        public async Task BriefsSpotsAndShowsAllCountTowardTheirSponsor()
        {
            await db.ResetAdsAndShowsAsync();
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            await briefRepo.CreateOwnerAsync(sponsorId, "A cozy hardware shop", null, null, enabled: true, CancellationToken.None);
            await briefRepo.CreateOwnerAsync(sponsorId, "Tools for every project", null, null, enabled: true, CancellationToken.None);
            for (var i = 1; i <= 5; i++)
                await spotRepo.CreateAsync(Spot(sponsorId, $"Spot {i}", AdState.Draft), CancellationToken.None);

            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into station.show (name, slug, sponsor_id) values (@name, @slug, @sponsorId)",
                    new { name = "Sponsored Show", slug = "sponsored-show", sponsorId });

            var rows = await sponsorRepo.ListAsync(q: null, CancellationToken.None);

            var row = Assert.Single(rows, r => r.Sponsor.Id == sponsorId);
            Assert.Equal(2, row.Briefs);
            Assert.Equal(5, row.SpotsByState[AdState.Draft]);
            Assert.Equal(1, row.Shows);
        }

        [Fact]
        public async Task SpotsAreGroupedByState()
        {
            await db.ResetAdsAndShowsAsync();
            var sponsorRepo = Harness.SponsorRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            await spotRepo.CreateAsync(Spot(sponsorId, "Draft 1", AdState.Draft), CancellationToken.None);
            await spotRepo.CreateAsync(Spot(sponsorId, "Draft 2", AdState.Draft), CancellationToken.None);
            await spotRepo.CreateAsync(Spot(sponsorId, "Approved 1", AdState.Approved), CancellationToken.None);

            var rows = await sponsorRepo.ListAsync(q: null, CancellationToken.None);

            var row = Assert.Single(rows, r => r.Sponsor.Id == sponsorId);
            Assert.Equal(2, row.SpotsByState[AdState.Draft]);
            Assert.Equal(1, row.SpotsByState[AdState.Approved]);
            Assert.False(row.SpotsByState.ContainsKey(AdState.Ready));
        }

        [Fact]
        public async Task TheQFilterFoldsLikeNameKey()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            await Harness.SeedSponsorAsync(db, "North Side Grocers");
            await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            // Extra internal whitespace + upper case — sponsor_fold(@q) collapses/lowers it the same
            // way it collapsed/lowered the stored name_key, so the LIKE still matches.
            var rows = await repo.ListAsync(q: "NORTH   SIDE", CancellationToken.None);

            Assert.Single(rows, r => r.Sponsor.Name == "North Side Grocers");
        }

        [Fact]
        public async Task AQContainingLikeMetacharactersMatchesThemLiterallyNotAsWildcards()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            // "100%" folds to "100%" in name_key — a q of "100%" must match THIS row and never behave as
            // a LIKE wildcard that would also match e.g. "1000 Fresh Foods" or every row via a bare "%".
            await Harness.SeedSponsorAsync(db, "100% Fresh Foods");
            await Harness.SeedSponsorAsync(db, "1000 Fresh Foods");
            await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            var rows = await repo.ListAsync(q: "100%", CancellationToken.None);

            Assert.Single(rows, r => r.Sponsor.Name == "100% Fresh Foods");
        }
    }

    // ---------------------------------------------------------------------
    // GetAsync — the single-row read, including the xmin-derived Version token UpdateAsync guards on
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetAsyncReturnsTheRowOrNull(DatabaseFixture db)
    {
        [Fact]
        public async Task AnExistingSponsorReturnsANonEmptyVersion()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            var sponsor = await repo.GetAsync(sponsorId, CancellationToken.None);

            Assert.NotNull(sponsor);
            Assert.False(string.IsNullOrEmpty(sponsor!.Version));
        }

        [Fact]
        public async Task AnUnknownIdReturnsNull()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var sponsor = await repo.GetAsync(999_999_999, CancellationToken.None);

            Assert.Null(sponsor);
        }
    }

    // ---------------------------------------------------------------------
    // UpdateAsync — sparse edit under the xmin version guard; a pack-owned row's name is read-only
    // regardless of version (checked before the version compare, per SponsorRepository's own ordering)
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpdateAsyncAppliesASparseEditUnderVersionGuard(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEditWithTheCurrentVersionApplies()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var before = (await repo.GetAsync(sponsorId, CancellationToken.None))!;

            var edit = new SponsorEdit(
                Name: null, Tagline: "Fresh since 1987", About: null, Phone: null, Address: null,
                Website: null, Tone: null);
            var result = await repo.UpdateAsync(sponsorId, edit, before.Version, CancellationToken.None);

            var ok = Assert.IsType<SponsorWriteResult.Ok>(result);
            Assert.Equal("Fresh since 1987", ok.Sponsor.Tagline);
            Assert.Equal("Bramble & Fitch", ok.Sponsor.Name);
        }

        [Fact]
        public async Task AStaleVersionIsRefused()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var before = (await repo.GetAsync(sponsorId, CancellationToken.None))!;
            var edit = new SponsorEdit(
                Name: null, Tagline: "First edit", About: null, Phone: null, Address: null, Website: null, Tone: null);
            await repo.UpdateAsync(sponsorId, edit, before.Version, CancellationToken.None);

            // The same (now stale) version, tried again.
            var second = await repo.UpdateAsync(
                sponsorId,
                new SponsorEdit(Name: null, Tagline: "Second edit", About: null, Phone: null, Address: null, Website: null, Tone: null),
                before.Version,
                CancellationToken.None);

            Assert.IsType<SponsorWriteResult.VersionConflict>(second);
        }

        [Fact]
        public async Task RenamingAPackOwnedSponsorIsRefused()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var pack = await UpsertOnePackSponsorAsync(repo, "genwave-catalog", "Al's Diner", CancellationToken.None);

            var edit = new SponsorEdit(
                Name: "Al's Renamed Diner", Tagline: null, About: null, Phone: null, Address: null,
                Website: null, Tone: null);
            var result = await repo.UpdateAsync(pack.Id, edit, pack.Version, CancellationToken.None);

            Assert.IsType<SponsorWriteResult.NamePackOwned>(result);
        }

        [Fact]
        public async Task AnUnknownIdReturnsNotFound()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var edit = new SponsorEdit(
                Name: null, Tagline: "Fresh since 1987", About: null, Phone: null, Address: null,
                Website: null, Tone: null);

            var result = await repo.UpdateAsync(999_999_999, edit, "AAAAAAAAAAAAAAA=", CancellationToken.None);

            Assert.IsType<SponsorWriteResult.NotFound>(result);
        }

        [Fact]
        public async Task RenamingToAFoldEqualExistingNameIsRefusedAsNameTaken()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            await Harness.SeedSponsorAsync(db, "North Side Grocers");
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var before = (await repo.GetAsync(sponsorId, CancellationToken.None))!;

            var edit = new SponsorEdit(
                Name: "north   side grocers", Tagline: null, About: null, Phone: null, Address: null,
                Website: null, Tone: null);
            var result = await repo.UpdateAsync(sponsorId, edit, before.Version, CancellationToken.None);

            Assert.IsType<SponsorWriteResult.NameTaken>(result);
        }
    }

    // ---------------------------------------------------------------------
    // STORY-409 (mechanics owed here since T432 owns the store) — pause/resume idempotency: pausing an
    // already-paused row leaves paused_at stable; resuming always clears it
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSetPausedAsyncIsIdempotentAndStampsPausedAt(DatabaseFixture db)
    {
        [Fact]
        public async Task PausingTwiceLeavesPausedAtStableAfterTheFirst()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            var first = await repo.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            Assert.NotNull(first!.PausedAt);

            var second = await repo.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);

            Assert.Equal(first.PausedAt, second!.PausedAt);
        }

        [Fact]
        public async Task ResumingClearsPausedAt()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await repo.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);

            var resumed = await repo.SetPausedAsync(sponsorId, paused: false, CancellationToken.None);

            Assert.NotNull(resumed);
            Assert.False(resumed!.Paused);
            Assert.Null(resumed.PausedAt);
        }
    }

    // ---------------------------------------------------------------------
    // STORY-410 — the delete guard: unreferenced sponsors delete outright; a referenced sponsor is
    // refused with the exact counts and up to ten referencing titles
    // ---------------------------------------------------------------------
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeleteIfUnreferencedAsyncGuardsAgainstReferencingRows(DatabaseFixture db)
    {
        static NewAdSpot Spot(long sponsorId, string title) =>
            new(
                sponsorId, title, Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
                PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null, InitialState: AdState.Draft,
                FailReason: null);

        [Fact]
        public async Task AnUnreferencedSponsorDeletes()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            var result = await repo.DeleteIfUnreferencedAsync(sponsorId, CancellationToken.None);

            Assert.IsType<SponsorDeleteResult.Deleted>(result);
            Assert.Null(await repo.GetAsync(sponsorId, CancellationToken.None));
        }

        [Fact]
        public async Task AnUnknownIdReturnsNotFound()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.SponsorRepo(db);

            var result = await repo.DeleteIfUnreferencedAsync(999_999_999, CancellationToken.None);

            Assert.IsType<SponsorDeleteResult.NotFound>(result);
        }

        [Fact]
        public async Task AReferencedSponsorRefusesWithCountsAndTitles()
        {
            // 3 briefs + 8 spots + 1 show = 12 referencing rows — more than ReadReferencesAsync's own
            // `limit 10` on Titles, so this exercises the cap: Briefs/Spots/Shows stay exact (uncapped,
            // each its own `count(*)`) while Titles truncates to 10.
            await db.ResetAdsAndShowsAsync();
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            for (var i = 1; i <= 3; i++)
                await briefRepo.CreateOwnerAsync(sponsorId, $"Brief {i}", null, null, enabled: true, CancellationToken.None);
            for (var i = 1; i <= 8; i++)
                await spotRepo.CreateAsync(Spot(sponsorId, $"Spot {i}"), CancellationToken.None);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into station.show (name, slug, sponsor_id) values (@name, @slug, @sponsorId)",
                    new { name = "Sponsored Show", slug = "sponsored-show", sponsorId });

            var result = await sponsorRepo.DeleteIfUnreferencedAsync(sponsorId, CancellationToken.None);

            var inUse = Assert.IsType<SponsorDeleteResult.InUse>(result);
            Assert.Equal(3, inUse.Briefs);
            Assert.Equal(8, inUse.Spots);
            Assert.Equal(1, inUse.Shows);
            Assert.Equal(10, inUse.Titles.Count);
            Assert.NotNull(await sponsorRepo.GetAsync(sponsorId, CancellationToken.None));
        }
    }
}
