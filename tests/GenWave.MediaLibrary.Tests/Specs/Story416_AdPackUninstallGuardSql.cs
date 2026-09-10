// STORY-416 — AdBriefRepository.UninstallPackAsync's own guard SQL + Postgres-backed outcomes
// (SPEC F172.4 · PLAN T437)
//
// Two halves, the Story401_VoicePackDeleteGuardSql.cs / Story406_SponsorStore.cs precedent
// combined: a pure text-pin on AdBriefRepository.UninstallGuardSql (no Postgres needed — a mutation
// that weakens the guard text itself has nowhere else to hide), plus real-Postgres facts proving
// UninstallPackAsync's three outcomes end to end (InUse leaves every row untouched; Deleted retires
// the pack's own spots and removes its briefs/sponsors; NotFound for a slug that was never
// installed) — the SponsorRepository.DeleteIfUnreferencedAsync precedent this method's own remarks
// name, one seam over.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdPackUninstallGuardSql
{
    // ---------------------------------------------------------------------
    // Text pin — AdBriefRepository.UninstallGuardSql itself
    // ---------------------------------------------------------------------
    public sealed class ScenarioTheGuardSqlNamesBothReferencingTablesAndExcludesThePacksOwnSpots
    {
        // Given AdBriefRepository's own uninstall guard text, When it is inspected directly.
        [Fact]
        public void ItSelectsFromStationAdSpotExactlyOnce() =>
            Assert.Equal(1, Occurrences(AdBriefRepository.UninstallGuardSql, "from station.ad_spot"));

        [Fact]
        public void ItSelectsFromStationShowExactlyOnce() =>
            Assert.Equal(1, Occurrences(AdBriefRepository.UninstallGuardSql, "from station.show"));

        [Fact]
        public void ItJoinsStationSponsorTwiceOnceForEachHalf() =>
            Assert.Equal(2, Occurrences(AdBriefRepository.UninstallGuardSql, "from station.sponsor"));

        [Fact]
        public void ItExcludesThePacksOwnSourcePackSpotsFromTheAdSpotHalf() =>
            Assert.Contains(
                "not (source = 'pack'::station.ad_source and pack_slug = @packSlug)",
                AdBriefRepository.UninstallGuardSql, StringComparison.Ordinal);

        [Fact]
        public void EachHalfCapsAtTenRows() =>
            Assert.Equal(2, Occurrences(AdBriefRepository.UninstallGuardSql, "limit 10"));
    }

    static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    // ---------------------------------------------------------------------
    // UninstallPackAsync against real Postgres — the three outcomes
    // ---------------------------------------------------------------------
    static NewAdSpot PackSpot(long sponsorId, string packSlug, string title) =>
        new(
            sponsorId, title, Brief: null, Script: null, AdSource.Pack,
            PackSlug: packSlug, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
            InitialState: AdState.Draft, FailReason: null);

    static NewAdSpot OwnerSpot(long sponsorId, string title) =>
        new(
            sponsorId, title, Brief: null, Script: null, AdSource.Owner,
            PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
            InitialState: AdState.Draft, FailReason: null);

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOwnerReferencedPackRefusesAndWritesNothing(DatabaseFixture db)
    {
        [Fact]
        public async Task AnOwnerAuthoredSpotNamingThePackSSponsorRefusesTheUninstall()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);
            var ownerSpot = await spotRepo.CreateAsync(OwnerSpot(sponsorId, "Owner's own Al's Diner spot"), CancellationToken.None);

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            var inUse = Assert.IsType<AdPackUninstallResult.InUse>(result);
            Assert.Contains(ownerSpot.Title, inUse.SpotTitles);
            Assert.Empty(inUse.ShowNames);

            // Nothing was removed — the brief and sponsor rows this call would otherwise have deleted
            // still exist.
            Assert.Contains(await briefRepo.ListAllAsync(CancellationToken.None), b => b.SponsorId == sponsorId);
            Assert.NotNull(await sponsorRepo.GetAsync(sponsorId, CancellationToken.None));
        }

        [Fact]
        public async Task AShowNamingThePackSSponsorRefusesTheUninstallToo()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);

            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into station.show (name, slug, sponsor_id) values (@name, @slug, @sponsorId)",
                    new { name = "Al's Diner Hour", slug = "als-diner-hour", sponsorId });

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            var inUse = Assert.IsType<AdPackUninstallResult.InUse>(result);
            Assert.Empty(inUse.SpotTitles);
            Assert.Contains("Al's Diner Hour", inUse.ShowNames);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioACleanUninstallRetiresThePacksSpotsAndRemovesItsBriefsAndSponsors(DatabaseFixture db)
    {
        [Fact]
        public async Task ItRetiresThePackSOwnSpotsAndDeletesItsBriefsAndSponsors()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);
            var packSpot = await spotRepo.CreateAsync(PackSpot(sponsorId, packSlug, "Al's Diner spot"), CancellationToken.None);

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            // The pack spot created above was itself just retired, and station.ad_spot.sponsor_id is
            // NOT NULL REFERENCES ... ON DELETE RESTRICT (db/46) — that spot still names its sponsor
            // after retirement, so the sponsor row survives (Sponsors: 0). See
            // AdPackUninstallResult.Deleted's own remarks.
            var deleted = Assert.IsType<AdPackUninstallResult.Deleted>(result);
            Assert.Equal(1, deleted.Briefs);
            Assert.Equal(0, deleted.Sponsors);
            Assert.Equal(1, deleted.RetiredSpots);

            var retired = await spotRepo.GetByIdAsync(packSpot.Id, CancellationToken.None);
            Assert.NotNull(retired);
            Assert.Equal(AdState.Retired, retired!.State);
            Assert.NotNull(retired.RetiredAt);

            Assert.DoesNotContain(await briefRepo.ListAllAsync(CancellationToken.None), b => b.SponsorId == sponsorId);
            Assert.NotNull(await sponsorRepo.GetAsync(sponsorId, CancellationToken.None));
        }

        [Fact]
        public async Task APackWithNoSpotHistoryDeletesItsSponsorsToo()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            // No spot ever referenced this sponsor, so nothing blocks its own deletion too
            // (STORY-416 AC1's own literal "sponsor count is 0" example).
            var deleted = Assert.IsType<AdPackUninstallResult.Deleted>(result);
            Assert.Equal(1, deleted.Briefs);
            Assert.Equal(1, deleted.Sponsors);
            Assert.Equal(0, deleted.RetiredSpots);

            Assert.DoesNotContain(await briefRepo.ListAllAsync(CancellationToken.None), b => b.SponsorId == sponsorId);
            Assert.Null(await sponsorRepo.GetAsync(sponsorId, CancellationToken.None));
        }
    }

    // "and state <> 'rendering'" — real Postgres, not the fake, since the whole point is proving the
    // retire UPDATE's own WHERE clause, not merely that some in-memory double remembers to skip it.
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARenderingPackSpotIsNeitherRetiredNorDeletedFrom(DatabaseFixture db)
    {
        [Fact]
        public async Task ARenderingPackSpotStaysRenderingAndItsSponsorEndsUpInKeptSponsors()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);
            var spotRepo = Harness.AdSpotRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);

            // IAdSpotStore.CreateAsync only accepts Draft/Approved/Failed as an InitialState — Rendering
            // is reached only through the real ClaimForPromotionAsync transition (Approved -> Rendering,
            // xmin-guarded), never fabricated directly, so this is the real render-claim path a worker
            // would actually take, not a shortcut around it.
            var approvedSpot = await spotRepo.CreateAsync(
                PackSpot(sponsorId, packSlug, "Al's Diner spot") with { InitialState = AdState.Approved },
                CancellationToken.None);
            var renderingSpot = await spotRepo.ClaimForPromotionAsync(approvedSpot.Id, approvedSpot.Version, CancellationToken.None);
            Assert.NotNull(renderingSpot);
            Assert.Equal(AdState.Rendering, renderingSpot!.State);

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            // AdSpotRepository.RetireAsync's own remarks state the invariant this uninstall's retire
            // step must honour too: "Rendering is deliberately absent from this list — it stays
            // undiscardable." Untouched by the retire step, the spot still names its sponsor, so
            // db/46's ON DELETE RESTRICT keeps that sponsor row standing — reported back via
            // KeptSponsors rather than attempted and failed.
            var deleted = Assert.IsType<AdPackUninstallResult.Deleted>(result);
            Assert.Equal(0, deleted.RetiredSpots);
            Assert.Equal(0, deleted.Sponsors);
            Assert.Contains(deleted.KeptSponsors, s => s.Id == sponsorId);

            var stillRendering = await spotRepo.GetByIdAsync(renderingSpot.Id, CancellationToken.None);
            Assert.NotNull(stillRendering);
            Assert.Equal(AdState.Rendering, stillRendering!.State);
            Assert.Null(stillRendering.RetiredAt);
        }
    }

    // ── An OWNER-authored brief, not just a spot, keeps a pack sponsor standing too: the pack brief
    // DELETE in AdBriefRepository.UninstallPackAsync is pack_slug-scoped and never touches an owner
    // brief on the same sponsor. Without the ad_brief survivor clause the sponsor DELETE would hit the
    // brief FK (23503) and the call would answer a nameless InUse — the clause keeps that sponsor as a
    // KeptSponsor instead, and this scenario proves it.
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOwnerBriefOnAPackSponsorKeepsItStanding(DatabaseFixture db)
    {
        [Fact]
        public async Task APackSponsorWithAnOwnerBriefAndNoSpotHistorySurvivesTheUninstall()
        {
            await db.ResetAdsAndShowsAsync();
            const string packSlug = "brought-to-you-by";
            var sponsorRepo = Harness.SponsorRepo(db);
            var briefRepo = Harness.AdBriefRepo(db);

            var sponsors = Assert.IsType<SponsorPackWriteResult.Ok>(
                await sponsorRepo.UpsertPackSponsorsAsync(packSlug, ["Al's Diner"], CancellationToken.None));
            var sponsorId = sponsors.Sponsors[0].Id;
            await briefRepo.UpsertAllAsync(
                packSlug, [new AdBriefUpsertInput(sponsorId, "Lunch special", null, null)], CancellationToken.None);

            // POST /api/ad-briefs' own root cause: an operator's own owner brief naming this SAME
            // pack sponsor, pack_slug null — a legitimate, supported state,
            // never hypothetical (AdBriefsController.Create accepts any sponsorId).
            var ownerBrief = await briefRepo.CreateOwnerAsync(
                sponsorId, "Book club specials", null, null, true, CancellationToken.None);
            Assert.NotNull(ownerBrief);

            var result = await briefRepo.UninstallPackAsync(packSlug, CancellationToken.None);

            var deleted = Assert.IsType<AdPackUninstallResult.Deleted>(result);
            Assert.Equal(1, deleted.Briefs);
            Assert.Equal(0, deleted.Sponsors);
            Assert.Contains(deleted.KeptSponsors, s => s.Id == sponsorId && s.Name == "Al's Diner");

            // The owner brief itself survives untouched — the pack brief DELETE is pack_slug-scoped,
            // this row's own pack_slug is null, so it was never a candidate for that delete at all.
            Assert.Contains(
                await briefRepo.ListAllAsync(CancellationToken.None),
                b => b.Id == ownerBrief!.Id && b.PackSlug is null);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnUninstalledSlugReturnsNotFound(DatabaseFixture db)
    {
        [Fact]
        public async Task NeitherTableEverHeldARowForThisSlug()
        {
            await db.ResetAdsAndShowsAsync();
            var briefRepo = Harness.AdBriefRepo(db);

            var result = await briefRepo.UninstallPackAsync("never-installed", CancellationToken.None);

            Assert.IsType<AdPackUninstallResult.NotFound>(result);
        }
    }
}
