// STORY-393 — Ad packs ride the shelf as data (F162.2)
//
// BDD specification — xUnit. AC1's first half (kind parses under its own kind folder), AC4/AC5
// (forward-compat drop, fail-closed SSRF posture), and the serializer's own reject arms drive pure
// in-process code directly (CatalogIndexValidator.TryValidate / CatalogAdPackManifestSerializer.Deserialize)
// — no DB, no HTTP, the Story269_CatalogKindSeam.cs idiom this file follows (same TryValidate helper
// shape, same Sha256Placeholder), fast and parallel-safe (T405 review F11 — split out of the
// Postgres-backed collection fixture they used to share with facts that never needed it). AC1's
// second half (the shelf endpoint's own detail projection) and AC2 (install upserts briefs) drive the
// REAL production route through WebApplicationFactory<Program> against a fake catalog origin — the
// Story337_IconPacksSwapTheChrome.cs idiom one pack-kind over — AC2 specifically against REAL
// Postgres (the Story392_AdBriefsApi.cs EphemeralStationDatabase idiom), since AdPackController.Install
// is the one catalog-install route in this codebase that performs a DURABLE write. AC3 re-pins
// "installed briefs still face the validator" at the INTEGRATION level: installs a pack whose one
// brief's brand collides with the shipped blocklist, reads back the exact persisted brand text, and
// feeds it through the REAL GenWave.Ads.AdScriptValidator (transitively referenced through
// GenWave.Host -> GenWave.Ads) — the generation-time gate
// GenWave.Ads.Tests.Specs.FeatureAdScriptWriterMeetsTheRealValidator/FeatureAdStockKeeping already
// prove AdSpotWorker wires into for EVERY sampled brief, pack or owner alike; this fact's own honest
// job is proving an ad-pack INSTALL never bypasses or waters down that same brand text before it
// gets there.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Ads;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdPackKind
{
    static readonly Uri Directory = new("https://catalog.test/repo/");

    // CatalogIndexValidator only checks a declared sha256's SHAPE (64 lowercase hex chars) — mirrors
    // Story269_CatalogKindSeam.cs's own Sha256Placeholder remarks verbatim.
    const string Sha256Placeholder = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static bool TryValidate(string indexJson, out IReadOnlyList<CatalogEntrySummary>? entries, out string? reason) =>
        CatalogIndexValidator.TryValidate(Encoding.UTF8.GetBytes(indexJson), Directory, out entries, out reason);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheKindParsesAndLists
    {
        [Fact]
        public void AnAdPackEntryValidatesUnderItsKindFolder()
        {
            // Given a catalog index with an entries/ad-packs/<slug>/ entry (SPEC F162.2's own
            // kind-folder shape),
            var index = """
                { "generatedAt": "2026-09-02", "entries": [
                  { "slug": "widget-world", "kind": "ad-pack", "audience": "everyone",
                    "manifest": { "path": "entries/ad-packs/widget-world/widget-world.ad-pack.json", "sha256": "SHA" },
                    "meta": { "path": "entries/ad-packs/widget-world/widget-world.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            // When the index is parsed,
            var success = TryValidate(index, out var entries, out _);

            // Then the entry validates under its own kind — the font/icon validator-arm precedent
            // (kind folder, slug parity), never a whole-index rejection.
            Assert.True(success);
            var entry = Assert.Single(entries!);
            Assert.Equal(CatalogEntryKind.AdPack, entry.Kind);
            Assert.Equal("entries/ad-packs/widget-world/widget-world.ad-pack.json", entry.Manifest.Path);
        }

        [Fact]
        public async Task TheShelfEndpointProjectsThePackWithItsBriefsSummarized()
        {
            // Given a catalog index serving one ad-pack entry with three briefs (composition-level —
            // WebApplicationFactory<Program> against a fake catalog origin, mirrors
            // IconPackInstallWebFactory's own idiom; no Postgres needed, this route never touches
            // station.ad_brief),
            await using var factory = new AdPackCatalogWebFactory(
                AdPackFixtures.BuildRoutedHandler(AdPackFixtures.ThreeBriefManifestJsonV1));
            var client = await AdPackCatalogWebFactory.LoggedInClientAsync(factory);

            // When the shelf loads the entry's own detail,
            var response = await client.GetAsync($"/api/catalog/entries/{AdPackFixtures.PackSlug}");

            // Then the pack renders with its briefs summarized (AC1) — every declared brief, brand
            // through structure, projected verbatim.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.NotNull(body);
            Assert.Equal("ad-pack", body!.Kind);
            Assert.Equal("Widget World", body.PackName);
            Assert.NotNull(body.AdPackBriefs);
            Assert.Equal(
                [
                    ("Bramble & Fitch", "A cozy hardware shop", "warm", "hook-offer-cta"),
                    ("Acme Filing Co", "Bureaucracy, but faster", (string?)null, (string?)null),
                    ("Nike", "The signature swoosh line", (string?)null, (string?)null),
                ],
                body.AdPackBriefs!.Select(b => (b.Brand, b.Premise, b.Tone, b.Structure)));
        }
    }

    [Collection(AdPackKindCollection.Name)]
    public sealed class ScenarioInstallUpsertsBriefs(AdPackKindArc arc)
    {
        [Fact]
        public void InstallingAThreeBriefPackYieldsThreeRows()
        {
            // Given a pack of three briefs, installed once,
            // Then station.ad_brief holds exactly three rows, one per (pack_slug, sponsor) (AC2).
            Assert.Equal(HttpStatusCode.OK, arc.FirstInstallStatus);
            Assert.Equal(3, arc.BriefRowCountAfterFirstInstall);
        }

        [Fact]
        public void ReinstallUpdatesInPlaceNeverDuplicates()
        {
            // Given the SAME pack installed a second time,
            // Then the row count is unchanged — updated, never duplicated (AC2).
            Assert.Equal(HttpStatusCode.OK, arc.SecondInstallStatus);
            Assert.Equal(3, arc.BriefRowCountAfterSecondInstall);
        }

        [Fact]
        public void ReinstallPreservesTheOperatorsDisable()
        {
            // Given the operator disabled one pack brief (via the real PATCH route) before the
            // reinstall — T405 review RULING: enabled is the operator's own lever, never the
            // manifest's business,
            Assert.Equal(HttpStatusCode.OK, arc.DisableStatus);

            // Then the reinstall never silently re-enables it.
            Assert.False(arc.EnabledAfterReinstall);
        }

        [Fact]
        public void ReinstallRefreshesThePremiseText()
        {
            // Then that SAME brief's own content still refreshes on reinstall — "content refreshes,
            // operator state persists" is one property, not a contradiction (T405 review F2).
            Assert.Equal(AdPackFixtures.RefreshedPremise, arc.PremiseAfterReinstall);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioForwardCompat
    {
        [Fact]
        public void AnUnknownKindDropsTheEntryAndKeepsTheIndex()
        {
            // Given an index containing an unknown kind alongside a valid ad-pack entry — the
            // shipped forward-compat pin (CatalogEntryKind precedent, Story269's own
            // ScenarioAnUnknownKindIsSkippedNotFatal) re-pinned over ad-pack: a pre-5.6.0 parser
            // reading this same index would drop the ad-pack entry the identical way (it names a
            // kind that parser has never heard of either) while every other entry still lists.
            var index = """
                { "generatedAt": "2026-09-02", "entries": [
                  { "slug": "future-hologram", "kind": "hologram", "audience": "everyone",
                    "manifest": { "path": "entries/future-hologram/future-hologram.hologram.json", "sha256": "SHA" },
                    "meta": { "path": "entries/future-hologram/future-hologram.meta.json", "sha256": "SHA" } },
                  { "slug": "widget-world", "kind": "ad-pack", "audience": "everyone",
                    "manifest": { "path": "entries/ad-packs/widget-world/widget-world.ad-pack.json", "sha256": "SHA" },
                    "meta": { "path": "entries/ad-packs/widget-world/widget-world.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            // When the index is parsed,
            var success = TryValidate(index, out var entries, out _);

            // Then that entry is dropped and every other entry — the ad-pack included — still lists
            // (AC4).
            Assert.True(success);
            Assert.Equal("widget-world", Assert.Single(entries!).Slug);
        }
    }

    public sealed class ScenarioFailClosedSsrfPosture
    {
        [Fact]
        public void AnAbsoluteAssetPathRejectsTheIndexWhole()
        {
            // Given a pack manifest whose path resolves outside the index directory (an absolute
            // URL masquerading as a relative path — the standing SSRF posture SPEC F90.2 already
            // enforces on every other kind),
            var index = """
                { "generatedAt": "2026-09-02", "entries": [
                  { "slug": "widget-world", "kind": "ad-pack", "audience": "everyone",
                    "manifest": { "path": "https://evil.test/entries/widget-world/widget-world.ad-pack.json", "sha256": "SHA" },
                    "meta": { "path": "entries/ad-packs/widget-world/widget-world.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            // When the index validates,
            var success = TryValidate(index, out _, out var reason);

            // Then the index is rejected whole (AC5) — never merely that one entry dropped.
            Assert.False(success);
            Assert.NotNull(reason);
        }

        [Fact]
        public void ASlugMismatchedManifestPathRejectsTheIndexWhole()
        {
            // Given a pack manifest sitting under a DIFFERENT slug's own directory (still a
            // well-formed relative path, still resolving inside the index directory — the slug-parity
            // check this kind gets the SAME as every other, not merely the directory-containment one
            // above),
            var index = """
                { "generatedAt": "2026-09-02", "entries": [
                  { "slug": "widget-world", "kind": "ad-pack", "audience": "everyone",
                    "manifest": { "path": "entries/ad-packs/some-other-pack/widget-world.ad-pack.json", "sha256": "SHA" },
                    "meta": { "path": "entries/ad-packs/widget-world/widget-world.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            // When the index validates,
            var success = TryValidate(index, out _, out var reason);

            // Then the index is rejected whole (AC5).
            Assert.False(success);
            Assert.NotNull(reason);
        }
    }

    /// <summary>
    /// The serializer's own reject arms (T405 review F3 — previously ZERO direct coverage:
    /// CatalogAdPackManifestSerializer.Deserialize was only ever exercised indirectly, through a
    /// single well-formed fixture). Pure, in-process, no DB/HTTP — drives Deserialize directly, the
    /// same "test the seam directly" idiom ScenarioTheKindParsesAndLists' own first Fact already
    /// uses one level up for CatalogIndexValidator.
    /// </summary>
    public sealed class ScenarioTheManifestSerializerCapsRejectHonestly
    {
        static string ManifestJson(string briefsArrayJson) => $$"""{ "packName": "Test Pack", "briefs": {{briefsArrayJson}} }""";

        static string Brief(string brand, string? premise = null) =>
            premise is null
                ? $$"""{ "brand": "{{brand}}" }"""
                : $$"""{ "brand": "{{brand}}", "premise": "{{premise}}" }""";

        [Fact]
        public void MoreThanTheBriefCountCapFailsToParse()
        {
            // Given one brief over CatalogAdPackManifestSerializer.MaxBriefsPerPack,
            var briefs = string.Join(
                ",", Enumerable.Range(0, CatalogAdPackManifestSerializer.MaxBriefsPerPack + 1).Select(i => Brief($"Brand {i}")));

            // When the manifest is parsed,
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{briefs}]"));

            // Then the whole manifest fails to parse.
            Assert.Null(manifest);
        }

        [Fact]
        public void AMissingBriefsArrayFailsToParse()
        {
            var manifest = CatalogAdPackManifestSerializer.Deserialize("""{ "packName": "Test Pack" }""");

            Assert.Null(manifest);
        }

        [Fact]
        public void AnEmptyBriefsArrayFailsToParse()
        {
            // A pack IS its briefs (the all-or-nothing posture every pack-shaped kind shares) —
            // zero declared briefs is not a legal, if boring, pack; it is a malformed one.
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson("[]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void ABlankBrandFailsToParse()
        {
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{Brief("   ")}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void ABrandOverTheLengthCapFailsToParse()
        {
            var overlong = new string('a', CatalogAdPackManifestSerializer.MaxBrandLength + 1);

            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{Brief(overlong)}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void AHintOverTheLengthCapFailsToParse()
        {
            var overlong = new string('a', CatalogAdPackManifestSerializer.MaxHintLength + 1);

            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{Brief("Acme", overlong)}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void MalformedJsonDegradesToNullNeverThrows()
        {
            var manifest = CatalogAdPackManifestSerializer.Deserialize("not json at all");

            Assert.Null(manifest);
        }

        [Fact]
        public void OneOverCapBriefAmongThreeFailsTheWholeManifestAllOrNothing()
        {
            // Given three otherwise-valid briefs, the MIDDLE one over the brand length cap — never
            // the first or last, so this can't pass by accident of iteration order,
            var overlongBrand = new string('a', CatalogAdPackManifestSerializer.MaxBrandLength + 1);
            var briefs = string.Join(",", new[] { Brief("First Brand"), Brief(overlongBrand), Brief("Third Brand") });

            // When the manifest is parsed,
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{briefs}]"));

            // Then the WHOLE manifest fails — never two-out-of-three admitted.
            Assert.Null(manifest);
        }

        [Fact]
        public void TwoBriefsRepeatingTheIdenticalBrandFailToParse()
        {
            // Given two briefs naming the EXACT SAME brand string (round-4 review blocker — HEAD
            // admitted this and AdPackController.cs's own dictionary-keyed response projection then
            // threw ArgumentException after the write already committed),
            var briefs = string.Join(",", new[] { Brief("Acme"), Brief("Acme") });

            // When the manifest is parsed,
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{briefs}]"));

            // Then the WHOLE manifest fails to parse — refused BEFORE any write, never partially
            // admitted.
            Assert.Null(manifest);
        }

        [Fact]
        public void TheSameManifestWithADistinctSecondBrandParsesFine()
        {
            // Given the identical two-brief shape as the Fact above, EXCEPT the second brief now names
            // a genuinely different brand string — the teeth for the check above: it must not be so
            // broad it rejects an ordinary two-brand manifest.
            var briefs = string.Join(",", new[] { Brief("Acme"), Brief("Widget World") });

            // When the manifest is parsed,
            var manifest = CatalogAdPackManifestSerializer.Deserialize(ManifestJson($"[{briefs}]"));

            // Then it parses cleanly, both brands intact.
            Assert.NotNull(manifest);
            Assert.Equal(["Acme", "Widget World"], manifest!.Briefs.Select(b => b.Brand));
        }
    }

    [Collection(AdPackKindCollection.Name)]
    public sealed class ScenarioInstalledBriefsFaceGeneration(AdPackKindArc arc)
    {
        [Fact]
        public async Task AnInstalledBriefStillFacesTheBrandBlocklist()
        {
            // Given an installed brief whose brand collides with the shipped blocklist — read back
            // VERBATIM off station.ad_brief, the exact row AdPackController.Install just wrote via
            // IAdBriefStore.UpsertAllAsync (T398's own upsert seam, T405's own batch widening; T399's
            // scope sweep already pins the validator's own brand-collision arm, and GenWave.Ads.Tests'
            // own AdSpotWorker facts already pin the full generation-path wiring — this fact's own
            // honest job is proving THIS brief's own installed brand text reaches that SAME real gate
            // unmodified),
            var installedBrand = arc.InstalledBlocklistedBrand;
            Assert.Equal("Nike", installedBrand);

            var rawScript =
                $"ANNOUNCER: {installedBrand} has the deal of the year, don't miss it.\n" +
                "VOICE1: Stop by today and see for yourself.";
            var request = new AdScriptValidationRequest(
                Posture: AudiencePosture.Everyone, MaxLineChars: 300, SpotSeconds: 30, ToleranceRatio: 0.4);

            // When generation uses it — the REAL AdScriptValidator.Validate, the exact gate
            // AdSpotWorker.GenerateOneAsync's own delegate closes over for every sampled brief,
            // pack or owner alike,
            var result = AdScriptValidator.Validate(rawScript, request, new RollingPatterDurationEstimator());

            // Then the script is refused exactly like any other collision (AC3).
            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }
    }

    /// <summary>
    /// Round-4 review blocker — the route-level re-pin: a manifest whose briefs repeat the identical
    /// brand string must never reach either write (<c>station.sponsor</c>, <c>station.ad_brief</c>)
    /// through the REAL <c>POST /api/ad-packs/{slug}/install</c> route, not merely at the serializer
    /// seam <see cref="ScenarioTheManifestSerializerCapsRejectHonestly"/> already pins directly. Reads
    /// <see cref="AdPackKindArc"/>'s own THIRD install (its own slug, never colliding with the two
    /// installs AC2/AC3 already exercise) — the Postgres-backed arc idiom this file already runs,
    /// reused rather than a whole second ephemeral database for one more install.
    /// </summary>
    [Collection(AdPackKindCollection.Name)]
    public sealed class ScenarioDuplicateBrandRefusesTheWholeManifest(AdPackKindArc arc)
    {
        [Fact]
        public void ARepeatedBrandInTheManifestRefusesWithA400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.DuplicateBrandInstallStatus);
        }

        [Fact]
        public void NothingIsWrittenForThatSlug()
        {
            // Then the parse-time refusal happened BEFORE either write — zero rows in either table for
            // this slug, never a partial commit.
            Assert.Equal(0, arc.SponsorRowCountForDuplicateBrandPack);
            Assert.Equal(0, arc.BriefRowCountForDuplicateBrandPack);
        }
    }
}

// ── The install/DB-backed arc — one Postgres, three fresh app instances (one per install), a
// PATCH-disable in between ─────────────────────────────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class AdPackKindCollection : ICollectionFixture<AdPackKindArc>
{
    public const string Name = "Story393AdPackKind";
}

/// <summary>
/// Arranges every DB-backed fact STORY-393's AC2/AC3 Scenarios read (the Story392AdBriefsApi
/// "arrange once, many read-only Scenarios" idiom, one pack-kind over): boots ONE real ephemeral
/// Postgres, then installs the pack TWICE through TWO SEPARATE, fresh <see cref="WebApplicationFactory{TEntryPoint}"/>
/// instances against the SAME database (T405 review F2 — a genuinely COLD <c>CatalogProxyService</c>
/// cache per install is what forces the second install to actually re-fetch the SECOND manifest
/// version, the honest alternative to advancing a fake clock past the 15-minute cache TTL: two
/// separate app boots is exactly how two genuinely separate installs — on two different days, or two
/// different station instances — would behave for real). Between the two installs, the operator
/// disables one pack brief through the REAL <c>PATCH /api/ad-briefs/{id}</c> route — proving the
/// PRESERVE-on-reinstall ruling (T405 review F1/F2) survives a real reinstall, not merely a
/// store-level unit call. Nothing here calls <c>AdBriefRepository</c>/<c>AdPackController</c>
/// directly; every write happens through the real HTTP route.
///
/// <para>
/// A THIRD install (round-4 review blocker's own route-level re-pin) rides the SAME already-running
/// Postgres — its own slug (<see cref="AdPackFixtures.DuplicateBrandPackSlug"/>), so it can never
/// collide with the two installs above, and its own fresh factory (the identical cold-cache
/// reasoning). A whole SECOND ephemeral database for one more install would be the heavier idiom this
/// type's own class remarks already reject once; reusing this arc's own Postgres is the cheaper
/// option that still proves the real route, not merely the serializer seam.
/// </para>
/// </summary>
public sealed class AdPackKindArc : IAsyncLifetime
{
    public HttpStatusCode FirstInstallStatus { get; private set; }
    public HttpStatusCode SecondInstallStatus { get; private set; }
    public int BriefRowCountAfterFirstInstall { get; private set; }
    public int BriefRowCountAfterSecondInstall { get; private set; }
    public HttpStatusCode DisableStatus { get; private set; }
    public bool EnabledAfterReinstall { get; private set; }
    public string PremiseAfterReinstall { get; private set; } = "";
    public string InstalledBlocklistedBrand { get; private set; } = "";
    public HttpStatusCode DuplicateBrandInstallStatus { get; private set; }
    public int SponsorRowCountForDuplicateBrandPack { get; private set; }
    public int BriefRowCountForDuplicateBrandPack { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story405AdPackDatabase is file-local (CS9051), the identical reason
        // Story392AdBriefsDatabase's own arc gives for the same shape.
        await using var database = await Story405AdPackDatabase.StartAsync();

        long disabledBriefId;

        // ── First install: the original three-brief pack, its own fresh app instance. ──
        await using (var factory1 = new AdPackInstallWebFactory(
                         database, AdPackFixtures.BuildRoutedHandler(AdPackFixtures.ThreeBriefManifestJsonV1)))
        {
            var client1 = await AdPackInstallWebFactory.LoggedInClientAsync(factory1);

            var first = await client1.PostAsync($"/api/ad-packs/{AdPackFixtures.PackSlug}/install", null);
            FirstInstallStatus = first.StatusCode;
            BriefRowCountAfterFirstInstall = await CountBriefRowsAsync(database.StationConnectionString, AdPackFixtures.PackSlug);

            // The operator disables ONE pack brief before the reinstall (T405 review F1/F2's own
            // PRESERVE-on-reinstall ruling) — through the REAL PATCH route, the same lever an
            // operator actually has, never a raw-SQL shortcut.
            disabledBriefId = await ReadBriefIdAsync(database.StationConnectionString, AdPackFixtures.PackSlug, AdPackFixtures.DisabledBrand);
            var disable = await client1.PatchAsync($"/api/ad-briefs/{disabledBriefId}", JsonContent.Create(new { enabled = false }));
            DisableStatus = disable.StatusCode;
        }

        // ── Second install (reinstall): a DIFFERENT premise for the disabled brand, served by a
        // FRESH app instance — see this type's own class remarks for why a cold cache, not a fake
        // clock. The operator's own disable must survive it. ──
        await using (var factory2 = new AdPackInstallWebFactory(
                         database, AdPackFixtures.BuildRoutedHandler(AdPackFixtures.ThreeBriefManifestJsonV2)))
        {
            var client2 = await AdPackInstallWebFactory.LoggedInClientAsync(factory2);

            var second = await client2.PostAsync($"/api/ad-packs/{AdPackFixtures.PackSlug}/install", null);
            SecondInstallStatus = second.StatusCode;
            BriefRowCountAfterSecondInstall = await CountBriefRowsAsync(database.StationConnectionString, AdPackFixtures.PackSlug);
        }

        (EnabledAfterReinstall, PremiseAfterReinstall) = await ReadEnabledAndPremiseAsync(database.StationConnectionString, disabledBriefId);
        InstalledBlocklistedBrand = await ReadBrandAsync(database.StationConnectionString, AdPackFixtures.PackSlug, "Nike");

        // ── Third install: a manifest whose two briefs repeat the identical brand — its own slug, its
        // own fresh factory, the SAME already-running database (this type's own class remarks explain
        // why reusing it rather than a second ephemeral Postgres). ──
        await using (var factory3 = new AdPackInstallWebFactory(
                         database,
                         AdPackFixtures.BuildRoutedHandler(AdPackFixtures.DuplicateBrandPackSlug, AdPackFixtures.DuplicateBrandManifestJson)))
        {
            var client3 = await AdPackInstallWebFactory.LoggedInClientAsync(factory3);

            var duplicate = await client3.PostAsync($"/api/ad-packs/{AdPackFixtures.DuplicateBrandPackSlug}/install", null);
            DuplicateBrandInstallStatus = duplicate.StatusCode;
        }

        SponsorRowCountForDuplicateBrandPack =
            await CountSponsorRowsAsync(database.StationConnectionString, AdPackFixtures.DuplicateBrandPackSlug);
        BriefRowCountForDuplicateBrandPack =
            await CountBriefRowsAsync(database.StationConnectionString, AdPackFixtures.DuplicateBrandPackSlug);
    }

    // Every helper below takes the connection STRING, not the file-local Story405AdPackDatabase
    // itself (CS9051: a file-local type cannot appear in a member signature of this non-file-local
    // class) — the connection string is the only piece of that type any of them actually need.

    static async Task<int> CountBriefRowsAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from station.ad_brief where pack_slug = @packSlug", new { packSlug });
    }

    static async Task<int> CountSponsorRowsAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from station.sponsor where pack_slug = @packSlug", new { packSlug });
    }

    static async Task<long> ReadBriefIdAsync(string stationConnectionString, string packSlug, string brand)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<long>(
            """
            select b.id from station.ad_brief b
            join station.sponsor s on s.id = b.sponsor_id
            where b.pack_slug = @packSlug and s.name = @brand
            """,
            new { packSlug, brand });
    }

    static async Task<(bool Enabled, string Premise)> ReadEnabledAndPremiseAsync(string stationConnectionString, long id)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<(bool, string)>(
            "select enabled, premise from station.ad_brief where id = @id", new { id });
    }

    static async Task<string> ReadBrandAsync(string stationConnectionString, string packSlug, string brand)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<string>(
            """
            select s.name from station.ad_brief b
            join station.sponsor s on s.id = b.sponsor_id
            where b.pack_slug = @packSlug and s.name = @brand
            """,
            new { packSlug, brand });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the AC1 shelf-endpoint fact alone — boots the
/// real Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="AdPackFixtures.IndexUrl"/> (served by a fake origin), NO real Postgres (a bogus
/// connection string never actually reached — <c>GET /api/catalog/entries/{slug}</c> never touches
/// <c>station.ad_brief</c>) — mirrors <c>IconPackInstallWebFactory</c>'s own shape one file over,
/// simpler still: no store to fake or swap in, this route reads nothing but the catalog proxy.
/// </summary>
file sealed class AdPackCatalogWebFactory(FakeHttpMessageHandler handler) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story393-adpack-catalog";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", AdPackFixtures.IndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="AdPackKindArc"/>'s own AC2/AC3
/// installs — boots the real Program.cs graph against a REAL ephemeral Postgres
/// (<paramref name="database"/>) with every hosted service removed (no background reach into
/// <c>station.ad_brief</c> racing this arc's own installs — the <c>Story392AdBriefsWebFactory</c>
/// idiom one controller over) and <c>Community:CatalogIndexUrl</c> pointed at the fake catalog
/// origin. <see cref="AdPackKindArc"/> constructs THREE of these, one per install (that type's own
/// class remarks explain why) — all against the SAME <paramref name="database"/>, each with its own
/// fresh, cold <c>CatalogProxyService</c> cache.
/// </summary>
file sealed class AdPackInstallWebFactory(Story405AdPackDatabase database, FakeHttpMessageHandler handler)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story393-adpack-install";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", database.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", database.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Community:CatalogIndexUrl", AdPackFixtures.IndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t405"</c> compose
/// project-name prefix this file's own arc needs.</summary>
file sealed class Story405AdPackDatabase : EphemeralStationDatabase
{
    Story405AdPackDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story405AdPackDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t405");
        var db = new Story405AdPackDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own Facts (mirrors
/// <c>IconPackInstallFixtures</c>' own idiom) — one valid <c>kind:"ad-pack"</c> entry, three briefs,
/// one of which ("Nike") is a real, bare, single-word blocklist entry (SPEC F160.3's own
/// <c>BrandBlocklist.txt</c> — a name with no ordinary dictionary sense, so it can only ever match as
/// the brand it is, never a false positive off ordinary copy) for the AC3 fact to install and
/// re-pin. No <c>assets[]</c> at all (SPEC F162.2 — an ad-pack carries no binary assets).
/// <see cref="ThreeBriefManifestJsonV1"/>/<see cref="ThreeBriefManifestJsonV2"/> (T405 review F2)
/// are the SAME three brands with ONE premise refreshed — the reinstall content-change fixture pair.
/// <see cref="DuplicateBrandPackSlug"/>/<see cref="DuplicateBrandManifestJson"/> (round-4 review
/// blocker) are a SECOND, independent pack whose two briefs repeat the identical brand — its own slug
/// so <see cref="AdPackKindArc"/>'s third install can never collide with the two installs above.
/// </summary>
file static class AdPackFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";

    public const string PackSlug = "widget-world";

    /// <summary>Round-4 review blocker's own route-level fixture — a SECOND pack, entirely disjoint
    /// from <see cref="PackSlug"/>, so <see cref="AdPackKindArc"/>'s third install proves ZERO rows
    /// land for THIS slug without disturbing the row counts the first two installs already pin.</summary>
    public const string DuplicateBrandPackSlug = "duplicate-brand-pack";

    /// <summary>The one brand F2's reinstall facts disable and change the premise of.</summary>
    public const string DisabledBrand = "Bramble & Fitch";

    /// <summary>The second install's own refreshed premise text for <see cref="DisabledBrand"/> —
    /// asserted verbatim by <c>ReinstallRefreshesThePremiseText</c>; kept in sync with the literal
    /// premise text inside <see cref="ThreeBriefManifestJsonV2"/>'s own JSON body by hand, the same
    /// independently-authored fixture/expectation pairing every other document in this file
    /// has.</summary>
    public const string RefreshedPremise = "A cozy hardware shop, now with a loyalty program";

    public const string ThreeBriefManifestJsonV1 = """
        { "packName": "Widget World",
          "briefs": [
            { "brand": "Bramble & Fitch", "premise": "A cozy hardware shop", "tone": "warm", "structure": "hook-offer-cta" },
            { "brand": "Acme Filing Co", "premise": "Bureaucracy, but faster" },
            { "brand": "Nike", "premise": "The signature swoosh line" }
          ] }
        """;

    /// <summary>The SAME three brands, ONE premise refreshed (SPEC F162.2's own "content refreshes"
    /// reinstall contract, T405 review F2) — never touches enabled; PRESERVE-on-reinstall is the
    /// store's own job, not this fixture's.</summary>
    public const string ThreeBriefManifestJsonV2 = """
        { "packName": "Widget World",
          "briefs": [
            { "brand": "Bramble & Fitch", "premise": "A cozy hardware shop, now with a loyalty program", "tone": "warm", "structure": "hook-offer-cta" },
            { "brand": "Acme Filing Co", "premise": "Bureaucracy, but faster" },
            { "brand": "Nike", "premise": "The signature swoosh line" }
          ] }
        """;

    /// <summary>Round-4 review blocker's own route-level fixture — two briefs, the SAME brand string
    /// repeated verbatim; <c>CatalogAdPackManifestSerializer.Deserialize</c> must refuse this whole
    /// manifest before <see cref="AdPackController.Install"/> ever writes a row.</summary>
    public const string DuplicateBrandManifestJson = """
        { "packName": "Duplicate Brand Pack",
          "briefs": [
            { "brand": "Acme", "premise": "First angle" },
            { "brand": "Acme", "premise": "Second angle" }
          ] }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    const string MetaJson = """
        {"author":"Test Fixture","description":"An ad-pack for the install endpoint specs.","audience":"everyone","added":"2026-09-02"}
        """;

    static string IndexJson(string slug, string manifestJson) => $$"""
        { "generatedAt": "2026-09-02", "entries": [
          { "slug": "{{slug}}", "kind": "ad-pack", "audience": "everyone",
            "manifest": { "path": "entries/ad-packs/{{slug}}/{{slug}}.ad-pack.json", "sha256": "{{Sha256Hex(manifestJson)}}" },
            "meta": { "path": "entries/ad-packs/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" } } ] }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else —
    /// mirrors <c>IconPackInstallFixtures.BuildRoutedHandler</c>'s own idiom. Installs under
    /// <see cref="PackSlug"/> (T405's own AC2/AC3 fixture) — see the <paramref name="slug"/> overload
    /// below for a differently-slugged pack (round-4 review blocker's own duplicate-brand fixture).
    /// <paramref name="manifestJson"/> is fixed per handler instance (never mutated after
    /// construction) — <see cref="AdPackKindArc"/> gets its "content changed between installs"
    /// behavior from constructing a SECOND handler (and a SECOND app instance) with a SECOND manifest,
    /// never from mutating this one mid-test.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string manifestJson) =>
        BuildRoutedHandler(PackSlug, manifestJson);

    /// <summary>See the single-argument overload's own remarks — this overload additionally takes the
    /// SLUG the fixture serves under, so a second, disjoint pack (round-4 review blocker's own
    /// <see cref="DuplicateBrandPackSlug"/>) can be routed without colliding with <see cref="PackSlug"/>'s
    /// own document URLs.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string slug, string manifestJson)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(slug, manifestJson),
            [DirectoryUrl + "entries/ad-packs/" + slug + "/" + slug + ".ad-pack.json"] = manifestJson,
            [DirectoryUrl + "entries/ad-packs/" + slug + "/" + slug + ".meta.json"] = MetaJson,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
