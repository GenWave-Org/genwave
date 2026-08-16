// STORY-332 — Avatar packs into the library (SPEC F128.3/.4 · PLAN T293/T294)
//
// BDD specification — xUnit. Install/uninstall/list only; the Wardrobe Avatars tab
// and transient shelf previews (AC3's UI half) live in admin-ui jest
// (wardrobe-avatar-packs.spec.tsx) + the T301 wire. The T294 GET /api/avatar-packs listing route's
// own Facts (ScenarioTheInstalledPacksListing, below) join this file rather than a new one — same
// controller, same FakeAvatarPackStore/AvatarPackInstallWebFactory harness, the Story284_FontPackLibrary.cs
// precedent of a listing route's Facts living beside its sibling install/uninstall route only applies
// there because THAT listing route sits in its OWN controller file's own Story number; this one is the
// SAME controller PLAN T294 widened, not a new one.
//
// WIRED T293 — every Fact below drives the real production route through WebApplicationFactory<Program>
// (real routing/auth/content-negotiation pipeline, real ffmpeg via the real ImageNormalizeService — no
// mock of the re-validation pipeline itself, mirrors Story333_TheWornFace.cs's own posture) against a
// fake catalog origin (mirrors Story282_FontPackInstall.cs's own FontPackInstallWebFactory idiom) and
// FakeAvatarPackStore/FakePersonaAvatarStore (this project has no Postgres fixture; the REAL
// station.avatar_pack(+_item) SQL — including the true no-partial-installs rollback — is T290's own
// coverage against real Postgres).
//
// The rider fact (ScenarioTheWidenedFetchCapAdmitsAPngOver256Kib) pins the T292-round-2 condition this
// task closed: CatalogProxyService.MaxAssetBytes (256 KiB) used to cap EVERY kind's real fetch even
// though CatalogIndexValidator already admitted a 512 KiB avatar PNG at index-validation time — a
// spec-legal 256-512 KiB asset used to 502 Oversize the moment install actually tried to fetch it.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Images;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureAvatarPacksIntoTheLibrary
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstallLandsThePack
    {
        [Fact]
        public async Task EveryItemIsStoredWithItsHashVerifiedBytes()
        {
            // Given a fake origin serving one real, already-square PNG item, with the index's own
            // asset sha256 computed from the REAL served bytes (so the fetch hash-verifies cleanly),
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When POST /api/avatar-packs/{slug}/install is called (the real production route),
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);

            // Then it responds success, and the stored item's own sha256 genuinely describes its own
            // stored bytes — proving the hash was computed over what actually landed, not merely
            // copied through from the index. Read through GetBySlugAsync (review finding S4/B1) — the
            // real IAvatarPackStore.GetAllAsync contract carries item name/suggestedPersona metadata
            // but NEVER bytes (a shelf-listing read, FakeAvatarPackStore now mirrors that shape
            // exactly); GetBySlugAsync is the one-pack, bytes-carrying detail read shape-identical
            // between this fake and the real repository — the only read this Fact's own bytes
            // assertion can use.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var pack = await store.GetBySlugAsync(AvatarPackInstallFixtures.PackSlug, CancellationToken.None);
            var item = Assert.Single(pack?.Items ?? []);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(item.Bytes)), item.Sha256);
        }

        [Fact]
        public async Task EveryStoredPngWasReValidatedServerSide()
        {
            // Given a fake origin serving a real, VALID, but NON-512-SQUARE PNG (300×400) — a shape a
            // pass-through "trust the index" implementation would happily store unchanged,
            var itemBytes = TestImages.CreatePng(300, 400);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the STORED bytes are a fresh 512×512 PNG, not the original 300×400 ones — proof the
            // T291 pipeline genuinely re-ran server-side rather than trusting the catalog's own CI.
            // Read through GetBySlugAsync (review finding S4 — see the sibling Fact above).
            var pack = await store.GetBySlugAsync(AvatarPackInstallFixtures.PackSlug, CancellationToken.None);
            var item = Assert.Single(pack?.Items ?? []);
            var (width, height) = await ProbePngDimensionsAsync(item.Bytes);
            Assert.Equal((512, 512), (width, height));
        }

        [Fact]
        public async Task AValidlyShapedSuggestedPersonaHintIsStoredVerbatim()
        {
            // Given a manifest item declaring a WELL-SHAPED suggestedPersona hint — the FOR-VALID-
            // SHAPES half of review finding S2's gate (the sibling Scenario later in this file pins
            // the OTHER half: a malformed hint degrades to null rather than travelling through).
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the hint is stored verbatim (an OFFER, never auto-applied — SPEC F128.5). Read
            // through GetBySlugAsync (review finding S4 — see this Scenario's own first Fact).
            var pack = await store.GetBySlugAsync(AvatarPackInstallFixtures.PackSlug, CancellationToken.None);
            var item = Assert.Single(pack?.Items ?? []);
            Assert.Equal(AvatarPackInstallFixtures.SuggestedPersonaSlug, item.SuggestedPersona);
        }
    }

    public sealed class ScenarioReinstallUpserts
    {
        [Fact]
        public async Task ASecondInstallReplacesRowsWithoutDuplicates()
        {
            // Given the same slug installed once already,
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When it is installed again,
            var second = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);

            // Then the install completes and the pack's rows are replaced, not duplicated.
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            Assert.Single(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioUninstallIsGuardFree
    {
        [Fact]
        public async Task ThePackRowsAreGone()
        {
            // Given an installed pack,
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When it is uninstalled,
            var response = await client.DeleteAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}");

            // Then it responds 204 and the pack row (+ its items) are gone.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AWornCopyOfOneOfItsFacesSurvivesUntouched()
        {
            // Given a persona already wearing a face (station.persona_avatar, seeded directly through
            // the T290 store — the copy model's whole point: this row never references the pack),
            var itemBytes = TestImages.CreatePng(512, 512);
            var packStore = new FakeAvatarPackStore();
            var personaAvatarStore = new FakePersonaAvatarStore();
            var wornFace = new PersonaAvatarInput(
                PersonaId: 7, Bytes: TestImages.CreatePng(512, 512), Sha256: "worn-face-sha",
                Token: "worn-face-token", Source: PersonaAvatarSource.Catalog, ImportedFrom: AvatarPackInstallFixtures.PackSlug);
            await personaAvatarStore.UpsertAsync(wornFace, CancellationToken.None);

            await using var factory = new AvatarPackInstallWebFactory(
                packStore, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes), personaAvatarStore: personaAvatarStore);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the pack is uninstalled,
            var response = await client.DeleteAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // Then the worn copy is untouched — the uninstall never reached station.persona_avatar at
            // all.
            var stillWorn = await personaAvatarStore.GetByPersonaIdAsync(7, CancellationToken.None);
            Assert.Equal(wornFace.Sha256, stillWorn?.Sha256);
        }
    }

    // ---------------------------------------------------------------------
    // T293 RIDER: the widened per-kind fetch cap
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheWidenedFetchCapAdmitsAPngOver256Kib
    {
        [Fact]
        public async Task AThreeHundredKibAvatarAssetInstallsSuccessfully()
        {
            // Given a real PNG padded (via a real, correctly-CRC'd tEXt chunk) to ~300 KiB — over the
            // OLD flat 256 KiB CatalogProxyService.MaxAssetBytes fetch cap every kind used to share,
            // but comfortably under the 512 KiB CatalogIndexValidator.MaxPngAssetBytes ceiling SPEC
            // F128.1 actually allows for a PNG-kind item,
            var itemBytes = BuildPngAtLeast(300 * 1024);
            Assert.InRange(itemBytes.Length, (256 * 1024) + 1, 512 * 1024);

            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted (the real production route, through the real
            // CatalogProxyService — no fetch-layer mock),
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);

            // Then it installs successfully — before the rider landed, CatalogProxyService.GetAssetAsync
            // would have withheld this asset as Oversize (502) the moment it tried to actually fetch it.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Single(await store.GetAllAsync(CancellationToken.None));
        }

        /// <summary>A real, valid PNG padded with a real tEXt chunk (mirrors
        /// <c>TestImages.WithTextChunk</c>'s own established use in Story333_TheWornFace.cs) until it is
        /// at least <paramref name="minBytes"/> long — the padding is stripped by the SAME re-encode
        /// that proves re-validation genuinely ran, so this fixture doubles as proof the pipeline
        /// tolerates a padded-but-legitimate PNG rather than merely a minimal one.</summary>
        static byte[] BuildPngAtLeast(int minBytes)
        {
            var plain = TestImages.CreatePng(512, 512);
            var padLength = Math.Max(0, minBytes - plain.Length);
            return TestImages.WithTextChunk(plain, "Comment", new string('a', padLength));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAHostileAssetNeverLands
    {
        [Fact]
        public async Task AFailedGateWritesNothingAndAnswersQuietly()
        {
            // Given a fake origin serving a REAL animated PNG (APNG) — the index's own sha256
            // correctly hashes the ACTUAL served bytes (a CI pass over hash/shape alone would admit
            // this: nothing about a bare sha256/bytes declaration can tell an APNG from a still PNG),
            var hostileBytes = TestImages.CreateApng(512, 512);
            Assert.Contains("acTL", TestImages.PngChunkTypes(hostileBytes));

            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(hostileBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused quietly (a client-error status, no gate name/reason leaked into the
            // body — F15.7) and NOTHING is stored — the T291 re-validation gate caught what the
            // catalog's own CI (and this route's own hash check) alone would have let through.
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest, $"expected a 4xx refusal, got {response.StatusCode}: {body}");
            Assert.DoesNotContain("acTL", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Animated", body, StringComparison.Ordinal);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioAllOrNothingAcrossMultipleItems
    {
        [Fact]
        public async Task ASecondItemFailingAGateWritesNothingViaTheStore()
        {
            // Given a two-item pack whose FIRST item is a genuinely valid PNG and whose SECOND is a
            // hostile APNG (review finding S3 — the "all-or-nothing" ProblemDetails/Fact above only
            // ever pinned a ONE-item pack; the UpsertCallCount counter FakeAvatarPackStore already
            // carries was never actually asserted on for a multi-item pack),
            var goodBytes = TestImages.CreatePng(512, 512);
            var badBytes = TestImages.CreateApng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: SecondItemFailsFixtures.BuildRoutedHandler(goodBytes, badBytes),
                catalogIndexUrl: SecondItemFailsFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted (the first item's own normalize call genuinely succeeds before
            // the second one fails — proving this is a REAL mid-pack refusal, not merely a first-item
            // rejection),
            var response = await client.PostAsync($"/api/avatar-packs/{SecondItemFailsFixtures.Slug}/install", null);

            // Then it refuses (400) and the store's own UpsertAsync was NEVER called — nothing from the
            // first, genuinely-good item ever reached the store either.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, store.UpsertCallCount);
        }
    }

    public sealed class ScenarioAWrongKindSlugRefuses
    {
        [Fact]
        public async Task AFontSlugPostedToAvatarInstallRefusesAsUnknownWithNothingStored()
        {
            // Given a catalog slug that resolves to a REAL, hash-verifiable entry — but a FONT-kind
            // one, not an avatar pack (review finding S8),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: WrongKindEntryFixtures.BuildRoutedHandler(), catalogIndexUrl: WrongKindEntryFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When that slug is POSTed to the AVATAR install route,
            var response = await client.PostAsync($"/api/avatar-packs/{WrongKindEntryFixtures.Slug}/install", null);

            // Then it is refused with the SAME "unknown pack" 404 a slug naming nothing at all would
            // get (this route has no business revealing that a non-avatar entry exists under this
            // slug) and nothing is stored.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioTheCatalogKillSwitchRefusesInstall
    {
        [Fact]
        public async Task ADisabledCatalogRefusesWithTheKillSwitchPostureAndNothingStored()
        {
            // Given the catalog kill switch (an empty Community:CatalogIndexUrl, SPEC F90.1 — mirrors
            // FontPackController's own Story282 sibling Fact, never previously pinned for this
            // controller, review finding S8),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, catalogIndexUrl: "");
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);

            // Then it responds a bare 404 (the same "surface does not exist" posture CatalogController's
            // own routes carry — never a ProblemDetails body) and nothing is stored.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioADuplicateItemNameRefuses
    {
        [Fact]
        public async Task AManifestListingTheSameItemNameTwiceRefusesWith400AndNothingStored()
        {
            // Given a manifest whose items[] names the SAME item TWICE (review finding S8 — pins the
            // duplicate-NAME check BuildRawItems's own remarks describe; distinct from the font suite's
            // own duplicate-FILE check, since an avatar pack's uniqueness key is scoped by name, not
            // file),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: DuplicateItemNameFixtures.BuildRoutedHandler(), catalogIndexUrl: DuplicateItemNameFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{DuplicateItemNameFixtures.Slug}/install", null);

            // Then it is refused (400) before anything reaches the store.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioUninstallOfAnUnknownSlugIs404
    {
        [Fact]
        public async Task DeleteOfASlugThatWasNeverInstalledIs404()
        {
            // Given no pack has ever been installed under this slug (review finding S8 — every other
            // uninstall Fact in this file first installs the pack it then deletes; this one never
            // does),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When DELETE /api/avatar-packs/{slug} is called for that never-installed slug,
            var response = await client.DeleteAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}");

            // Then it responds 404.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // T293 REVIEW ROUND 1 FINDINGS — S1 (bound the normalize stage) + S2 (shape the durable strings)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheItemCountCeiling
    {
        [Fact]
        public async Task AManifestOverTheItemCeilingRefusesWith400AndNothingWritten()
        {
            // Given a manifest declaring MaxPackItems + 1 items — every one legitimately naming the
            // SAME already-declared asset (review finding S1: an unbounded item COUNT, not merely an
            // unbounded fetched-byte total, is what could drive NormalizeAllItemsAsync's peak memory),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: OverCapManifestFixtures.BuildRoutedHandler(), catalogIndexUrl: OverCapManifestFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{OverCapManifestFixtures.Slug}/install", null);

            // Then it is refused (400, naming the item-ceiling rule) and nothing is stored — the
            // refusal happens before a single item ever reaches ImageNormalizeService.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioASharedImageNormalizesExactlyOnce
    {
        [Fact]
        public async Task TwoItemsSharingOneFileInvokeTheImageProcessRunnerOnce()
        {
            // Given a two-item manifest where BOTH items legitimately share the SAME manifest file
            // (BuildRawItems's own carve-out — never a store-level collision), with the real
            // ImageNormalizeService/FfmpegImageProcessRunner pipeline wired behind a COUNTING decorator
            // (review finding S1's own observable seam — a real re-encode still runs, so this proves
            // memoization, not merely that the gate was skipped),
            var itemBytes = TestImages.CreatePng(512, 512);
            var runner = new CountingRealImageProcessRunner();
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: TwoItemsOneFileFixtures.BuildRoutedHandler(itemBytes),
                catalogIndexUrl: TwoItemsOneFileFixtures.IndexUrl, imageProcessRunner: runner);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{TwoItemsOneFileFixtures.Slug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then both items land, but the underlying ffmpeg re-encode ran exactly ONCE — the second
            // item's own normalize call was served from the per-file memo cache, never re-invoking
            // IImageProcessRunner a second time for the identical already-fetched bytes.
            Assert.Equal(1, runner.InvocationCount);
        }
    }

    public sealed class ScenarioAnInvalidSuggestedPersonaDegradesToNull
    {
        [Fact]
        public async Task AMalformedSuggestedPersonaInstallsWithTheHintDroppedRatherThanRejecting()
        {
            // Given a manifest item whose suggestedPersona is NOT a real catalog-slug shape (review
            // finding S2 — mirrors CatalogController.ValidateSuggestedPersonaShape's own degrade
            // posture for the EPHEMERAL shelf projection, now applied to this DURABLE write path too),
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: InvalidSuggestedPersonaFixtures.BuildRoutedHandler(itemBytes),
                catalogIndexUrl: InvalidSuggestedPersonaFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{InvalidSuggestedPersonaFixtures.Slug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the install SUCCEEDS (a bad hint is never fatal — SPEC F128.5's "offer" posture) and
            // the malformed hint is dropped to null, never stored as-is.
            var pack = await store.GetBySlugAsync(InvalidSuggestedPersonaFixtures.Slug, CancellationToken.None);
            var item = Assert.Single(pack?.Items ?? []);
            Assert.Null(item.SuggestedPersona);
        }
    }

    public sealed class ScenarioAnItemNameOutsideTheAllowedShapeRefuses
    {
        [Fact]
        public async Task AnItemNameCarryingAControlCharacterRefusesWith400AndNothingStored()
        {
            // Given a manifest item whose name carries a REAL tab control character once parsed (review
            // finding S2, round 2 — InvalidItemNameFixtures's own remarks: the JSON-ESCAPED \t form is
            // what actually reaches CatalogAvatarPackManifestSerializer.Deserialize as a real, non-empty
            // Name that IsValidItemName must still catch — item.Name is a display string, not a slug,
            // but still gets a printable/length gate rather than flowing unshaped into
            // station.avatar_pack_item.name and, eventually, a response body),
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: InvalidItemNameFixtures.BuildRoutedHandler(itemBytes), catalogIndexUrl: InvalidItemNameFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{InvalidItemNameFixtures.Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused SPECIFICALLY by the item-name shape gate — 400 with a ProblemDetails.Detail
            // naming the shape rule, discriminating this refusal from CatalogInstallShell.MalformedManifestProblem's
            // generic "could not be parsed" 400 (the wrong 400 this fixture's earlier, unparseable-raw-tab
            // form used to trigger, never actually reaching IsValidItemName) — with the raw control
            // character never echoed into the body and nothing stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = JsonSerializer.Deserialize<ProblemDetails>(body);
            Assert.NotNull(problem);
            Assert.NotNull(problem.Detail);
            Assert.Contains("outside the allowed shape", problem.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("\t", body, StringComparison.Ordinal);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AnItemNameOverTheLengthCeilingRefusesWith400AndNothingStored()
        {
            // Given a manifest item whose name is JSON-legal on its own (no escaping needed, no control
            // character) but sits one character over the item-name length ceiling (review finding S2,
            // round 2 — the gate's LENGTH arm, never exercised by the sibling control-character Fact
            // above, so IsValidItemName's two arms each now have their own discriminating coverage),
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, handler: TooLongItemNameFixtures.BuildRoutedHandler(itemBytes), catalogIndexUrl: TooLongItemNameFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{TooLongItemNameFixtures.Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused by the SAME item-name shape gate (400, ProblemDetails.Detail naming the
            // shape rule — discriminating this from the malformed-manifest 400 exactly as the sibling
            // Fact above), with nothing stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = JsonSerializer.Deserialize<ProblemDetails>(body);
            Assert.NotNull(problem);
            Assert.NotNull(problem.Detail);
            Assert.Contains("outside the allowed shape", problem.Detail, StringComparison.Ordinal);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioSixMebiByteCeilingCutsOffEarly
    {
        [Fact]
        public async Task ThePackCeilingRefusesTheInstantTheRunningTotalCrossesItWithoutFetchingWhatFollows()
        {
            // Given an index declaring far more than 6 MiB across many PNG-ceilinged (512 KiB each)
            // assets, plus a "successor" asset only a "sum the total after every asset is already
            // fetched" implementation would go on to request too (review finding S8 — mirrors
            // FontPackController's own N1 rider, scaled to the avatar kind's 6 MiB/512-KiB-per-asset
            // arithmetic),
            var store = new FakeAvatarPackStore();
            var handler = SixMebiByteCeilingFixtures.BuildRoutedHandler();
            await using var factory = new AvatarPackInstallWebFactory(store, handler, catalogIndexUrl: SixMebiByteCeilingFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            await client.PostAsync($"/api/avatar-packs/{SixMebiByteCeilingFixtures.Slug}/install", null);

            // Then the successor's own URL was NEVER requested — the early-cutoff proof itself, read
            // off the fake handler's own recorded request log.
            Assert.DoesNotContain(
                handler.Requests, request => request.RequestUri!.AbsoluteUri == SixMebiByteCeilingFixtures.SuccessorAssetUrl);
        }

        [Fact]
        public async Task ThePackCeilingRefusalIs400WithNothingStored()
        {
            // Given the same over-ceiling pack,
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(
                store, SixMebiByteCeilingFixtures.BuildRoutedHandler(), catalogIndexUrl: SixMebiByteCeilingFixtures.IndexUrl);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/avatar-packs/{SixMebiByteCeilingFixtures.Slug}/install", null);

            // Then it is refused as over the ceiling (400) and nothing is stored.
            Assert.Equal(
                (HttpStatusCode.BadRequest, 0),
                (response.StatusCode, (await store.GetAllAsync(CancellationToken.None)).Count));
        }
    }

    // ---------------------------------------------------------------------
    // T294 — THE INSTALLED PACKS LISTING (GET /api/avatar-packs)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheInstalledPacksListing
    {
        [Fact]
        public async Task AnInstalledPackListsWithNameItemsAndProvenance()
        {
            // Given a pack installed through the real production install route (so the `definition`
            // this GET re-parses is the SAME jsonb the install route actually wrote — mirrors
            // Story284_FontPackLibrary.cs's own ScenarioTheLibraryListsInstalledPacks precedent),
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the library is listed,
            var response = await client.GetAsync("/api/avatar-packs");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var pack = Assert.Single(document.RootElement.EnumerateArray());
            var item = Assert.Single(pack.GetProperty("items").EnumerateArray());

            // Then it lists the pack's own manifest name, its one item's name/suggestedPersona, and
            // the db/25 provenance pair.
            Assert.Equal(
                (Status: HttpStatusCode.OK, Slug: AvatarPackInstallFixtures.PackSlug, Name: "Warm Grins",
                 ItemName: AvatarPackInstallFixtures.ItemName, ItemSuggestedPersona: AvatarPackInstallFixtures.SuggestedPersonaSlug,
                 ImportedFrom: AvatarPackInstallFixtures.PackSlug),
                (Status: response.StatusCode, Slug: pack.GetProperty("slug").GetString(), Name: pack.GetProperty("name").GetString(),
                 ItemName: item.GetProperty("name").GetString(), ItemSuggestedPersona: item.GetProperty("suggestedPersona").GetString(),
                 ImportedFrom: pack.GetProperty("importedFrom").GetString()));
        }

        [Fact]
        public async Task TheListingNeverCarriesItemBytes()
        {
            // Given the same installed pack,
            var itemBytes = TestImages.CreatePng(512, 512);
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store, handler: AvatarPackInstallFixtures.BuildRoutedHandler(itemBytes));
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/avatar-packs/{AvatarPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the library is listed,
            var response = await client.GetAsync("/api/avatar-packs");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var pack = Assert.Single(document.RootElement.EnumerateArray());
            var item = Assert.Single(pack.GetProperty("items").EnumerateArray());

            // Then neither the pack row nor its item carries a "bytes" member — this listing is
            // metadata only (mirrors FontLibraryPackDto's own "no face bytes on this wire" contract);
            // the Wardrobe's face grid reads bytes through the transient proxied catalog route
            // instead.
            var packMembers = pack.EnumerateObject().Select(p => p.Name).ToArray();
            var itemMembers = item.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.DoesNotContain("bytes", packMembers);
            Assert.DoesNotContain("bytes", itemMembers);
        }

        [Fact]
        public async Task NoInstalledPacksListsAnEmptyArrayNotAnError()
        {
            // Given no packs installed,
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store);
            var client = await AvatarPackInstallWebFactory.LoggedInClientAsync(factory);

            // When the library is listed,
            var response = await client.GetAsync("/api/avatar-packs");

            // Then it responds 200 with an empty array — the honest "nothing installed yet" shape,
            // never an error.
            Assert.Equal(
                (HttpStatusCode.OK, "[]"),
                (response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        [Fact]
        public async Task AnAnonymousRequestIsUnauthorized()
        {
            // Given no session cookie (mirrors Story284_FontPackLibrary.cs's own AnonymousAccess
            // Fact — this route carries the SAME AdminSurface+Settings pairing every other
            // api/avatar-packs route does; the Story278 route-set pin + Story289's broad sweep both
            // re-confirm this by name, this Fact just keeps the local, dedicated coverage this
            // controller's sibling routes already have),
            var store = new FakeAvatarPackStore();
            await using var factory = new AvatarPackInstallWebFactory(store);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the library is listed anonymously,
            var response = await client.GetAsync("/api/avatar-packs");

            // Then it is refused 401.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ── ffprobe verification helper — black-box: probe the produced bytes, never the service's own
    // internals (mirrors Story333_TheWornFace.cs's own ProbePngAsync idiom, narrowed to dims only). ──

    static async Task<(int Width, int Height)> ProbePngDimensionsAsync(byte[] pngBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"genwave-story332-probe-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, pngBytes);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("stream=width,height");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1");
            psi.ArgumentList.Add(path);

            using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            int? width = null;
            int? height = null;
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0])
                {
                    case "width": width = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "height": height = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); break;
                }
            }

            if (width is null || height is null)
                throw new InvalidOperationException($"ffprobe produced no usable stream info: {stdout}");

            return (width.Value, height.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — boots the real
/// Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="AvatarPackInstallFixtures.IndexUrl"/> (served by a fake origin, mirrors
/// Story282_FontPackInstall.cs's own <c>FontPackInstallWebFactory</c>), <see cref="IAvatarPackStore"/>
/// replaced by a <see cref="FakeAvatarPackStore"/>, and <see cref="IPersonaAvatarStore"/> replaced by a
/// <see cref="FakePersonaAvatarStore"/> (the worn-copy-survives Fact's own arrange seam).
/// <see cref="GenWave.Host.Images.ImageNormalizeService"/> is left WIRED to its real production
/// registration (real ffmpeg) — never faked, mirrors Story333_TheWornFace.cs's own posture.
/// <see cref="GenWave.Host.Images.IImageProcessRunner"/> stays production-wired too UNLESS
/// <paramref name="imageProcessRunner"/> hands in a counting decorator (review finding S1's own
/// memoization Fact) — the ONE Fact in this file that needs to observe ffmpeg's own invocation count
/// rather than merely its outcome.
/// </summary>
file sealed class AvatarPackInstallWebFactory(
    FakeAvatarPackStore? store = null, FakeHttpMessageHandler? handler = null,
    FakePersonaAvatarStore? personaAvatarStore = null, string catalogIndexUrl = AvatarPackInstallFixtures.IndexUrl,
    IImageProcessRunner? imageProcessRunner = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story332-avatarinstall";

    readonly FakeHttpMessageHandler handler = handler ?? AvatarPackInstallFixtures.BuildRoutedHandler(TestImages.CreatePng(512, 512));
    readonly FakeAvatarPackStore store = store ?? new FakeAvatarPackStore();
    readonly FakePersonaAvatarStore personaAvatarStore = personaAvatarStore ?? new FakePersonaAvatarStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IAvatarPackStore>();
            services.AddSingleton<IAvatarPackStore>(store);

            services.RemoveAll<IPersonaAvatarStore>();
            services.AddSingleton<IPersonaAvatarStore>(personaAvatarStore);

            if (imageProcessRunner is not null)
            {
                services.RemoveAll<IImageProcessRunner>();
                services.AddSingleton(imageProcessRunner);
            }
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
/// Fixture documents + a routed fake HTTP double for this file's own Facts — a single valid
/// kind:"avatar" entry with ONE manifest item, every sha256 computed from the served content itself.
/// <c>file</c>-scoped (mirrors <c>FontPackInstallFixtures</c>'s own established idiom).
/// <see cref="BuildRoutedHandler"/> is parameterized by the item's own bytes rather than fixed content
/// — every Fact above supplies whatever shape/size PNG (or hostile non-PNG) its own scenario needs.
/// </summary>
file static class AvatarPackInstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string PackSlug = "warm-grins";
    public const string ItemName = "Classic";
    public const string ItemFile = "classic.png";
    public const string SuggestedPersonaSlug = "flip";

    static string ManifestJson => $$"""
        {"packName":"Warm Grins","items":[
          {"name":"{{ItemName}}","file":"{{ItemFile}}","suggestedPersona":"{{SuggestedPersonaSlug}}"}
        ]}
        """;

    const string MetaJson = """
        {
          "author": "Test Fixture",
          "description": "A curated avatar pack for the install endpoint specs.",
          "audience": "everyone",
          "added": "2026-08-15"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] itemBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{PackSlug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{PackSlug}}/{{PackSlug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{PackSlug}}/{{PackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{PackSlug}}/{{ItemFile}}", "sha256": "{{Sha256Hex(itemBytes)}}", "bytes": {{itemBytes.Length}} }
            ] } ] }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else — the one
    /// binary asset URL serves <paramref name="itemBytes"/> verbatim, whatever shape the calling Fact
    /// gave it (a real square PNG, a real non-square one, a real APNG, or a padded one).</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] itemBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(itemBytes),
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + PackSlug + "/" + ItemFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(itemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>
/// A counting <see cref="IImageProcessRunner"/> DECORATOR over the real
/// <see cref="FfmpegImageProcessRunner"/> (review finding S1) — unlike
/// <c>GenWave.Host.Tests.Fakes.CountingImageProcessRunner</c> (which never touches ffmpeg at all, the
/// seam Story333_TheWornFace.cs's own "prove ffmpeg was never invoked" Facts need), this class still
/// DELEGATES every call through to a real ffmpeg run — <c>ScenarioASharedImageNormalizesExactlyOnce</c>'s
/// own Fact needs the SECOND item's own bytes to genuinely land (a real re-encoded PNG, not merely "the
/// gate was skipped"), so counting has to sit beside a real run, not replace one.
/// </summary>
file sealed class CountingRealImageProcessRunner : IImageProcessRunner
{
    readonly IImageProcessRunner inner = new FfmpegImageProcessRunner();

    public int InvocationCount { get; private set; }

    public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        InvocationCount++;
        return inner.RunAsync(args, ct);
    }
}

/// <summary>Fixture for <c>ScenarioAllOrNothingAcrossMultipleItems</c> (review finding S3) — a
/// two-item pack whose first item is a genuinely valid PNG and whose second is a hostile APNG, each its
/// own distinct file (so BOTH assets are genuinely fetched before either item is ever re-validated).
/// <c>file</c>-scoped (mirrors <see cref="AvatarPackInstallFixtures"/>'s own established idiom).</summary>
file static class SecondItemFailsFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/two-faces-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "two-faces";
    const string GoodItemName = "GoodFace";
    const string BadItemName = "BadFace";
    const string GoodFile = "good.png";
    const string BadFile = "bad.png";

    static string ManifestJson => $$"""
        {"packName":"Two Faces","items":[
          {"name":"{{GoodItemName}}","file":"{{GoodFile}}"},
          {"name":"{{BadItemName}}","file":"{{BadFile}}"}
        ]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"Item two fails re-validation.","audience":"everyone","added":"2026-08-15"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] goodBytes, byte[] badBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{GoodFile}}", "sha256": "{{Sha256Hex(goodBytes)}}", "bytes": {{goodBytes.Length}} },
              { "path": "entries/{{Slug}}/{{BadFile}}", "sha256": "{{Sha256Hex(badBytes)}}", "bytes": {{badBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] goodBytes, byte[] badBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(goodBytes, badBytes),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var goodUrl = Directory + "entries/" + Slug + "/" + GoodFile;
        var badUrl = Directory + "entries/" + Slug + "/" + BadFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == goodUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(goodBytes) });
            if (absoluteUri == badUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(badBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioAWrongKindSlugRefuses</c> (review finding S8) — a REAL, hash-
/// verifiable FONT-kind entry (a minimal but genuinely valid woff2-shaped asset declaration; its
/// content is never actually parsed as a font manifest by anything this route reaches, since the
/// kind-mismatch refusal fires immediately after CatalogInstallShell.ResolveEntryAsync, before the
/// avatar manifest is ever deserialized).</summary>
file static class WrongKindEntryFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/wrong-kind-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "not-an-avatar-pack";
    const string AssetFile = "face.woff2";

    const string ManifestJson = """
        {"family":"Wrong Kind","files":[{"role":"upright","file":"face.woff2","weight":"400","style":"normal","bytes":4}],"license":"OFL-1.1","sourceUrl":"https://example.test/wrong-kind","version":"1.0","subset":"text"}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A font pack, not an avatar pack.","audience":"everyone","added":"2026-08-15"}
        """;

    static readonly byte[] AssetBytes = "wof2"u8.ToArray();

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{AssetFile}}", "sha256": "{{Sha256Hex(AssetBytes)}}", "bytes": {{AssetBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".font.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + AssetFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(AssetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioADuplicateItemNameRefuses</c> (review finding S8) — two manifest
/// items sharing the SAME name but pointing at two DIFFERENT (both genuinely fetchable) files.</summary>
file static class DuplicateItemNameFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/dup-name-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "dup-name-pack";
    const string DuplicateName = "Classic";
    const string FileA = "a.png";
    const string FileB = "b.png";

    static string ManifestJson => $$"""
        {"packName":"Dup Name Pack","items":[
          {"name":"{{DuplicateName}}","file":"{{FileA}}"},
          {"name":"{{DuplicateName}}","file":"{{FileB}}"}
        ]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"Two items sharing one name.","audience":"everyone","added":"2026-08-15"}
        """;

    static readonly byte[] AssetBytes = TestImages.CreatePng(512, 512);

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{FileA}}", "sha256": "{{Sha256Hex(AssetBytes)}}", "bytes": {{AssetBytes.Length}} },
              { "path": "entries/{{Slug}}/{{FileB}}", "sha256": "{{Sha256Hex(AssetBytes)}}", "bytes": {{AssetBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var fileAUrl = Directory + "entries/" + Slug + "/" + FileA;
        var fileBUrl = Directory + "entries/" + Slug + "/" + FileB;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == fileAUrl || absoluteUri == fileBUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(AssetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioTheItemCountCeiling</c> (review finding S1) —
/// <see cref="AvatarPackController.MaxPackItems"/> + 1 manifest items, every one legitimately naming
/// the SAME already-declared asset (the shared-image carve-out, at a hostile scale).</summary>
file static class OverCapManifestFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/overcap-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "way-too-many";
    const string SharedFile = "shared.png";
    public const int ItemCount = AvatarPackController.MaxPackItems + 1;

    static string ManifestJson
    {
        get
        {
            var items = Enumerable.Range(0, ItemCount).Select(i => $$"""{"name":"item-{{i}}","file":"{{SharedFile}}"}""");
            return $$"""{"packName":"Way Too Many","items":[{{string.Join(",", items)}}]}""";
        }
    }

    const string MetaJson = """
        {"author":"Test Fixture","description":"A manifest declaring far too many items.","audience":"everyone","added":"2026-08-15"}
        """;

    // Never actually re-validated (the item-count ceiling refuses before BuildRawItems ever reaches
    // NormalizeAllItemsAsync) — a real, small PNG anyway, so a future gate-order change cannot
    // accidentally turn this fixture into a false pass.
    static readonly byte[] ItemBytes = TestImages.CreatePng(512, 512);

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{SharedFile}}", "sha256": "{{Sha256Hex(ItemBytes)}}", "bytes": {{ItemBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + SharedFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ItemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioASharedImageNormalizesExactlyOnce</c> (review finding S1) — two
/// DIFFERENTLY-NAMED items pointing at the SAME manifest file, the legitimate shared-image case
/// BuildRawItems's own remarks describe.</summary>
file static class TwoItemsOneFileFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/twin-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "twin-grins";
    const string ItemAName = "Classic";
    const string ItemBName = "Retro";
    const string SharedFile = "shared.png";

    static string ManifestJson => $$"""
        {"packName":"Twin Grins","items":[
          {"name":"{{ItemAName}}","file":"{{SharedFile}}"},
          {"name":"{{ItemBName}}","file":"{{SharedFile}}"}
        ]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"Two items sharing one image.","audience":"everyone","added":"2026-08-15"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] itemBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{SharedFile}}", "sha256": "{{Sha256Hex(itemBytes)}}", "bytes": {{itemBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] itemBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(itemBytes),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + SharedFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(itemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioAnInvalidSuggestedPersonaDegradesToNull</c> (review finding S2) —
/// one item whose <c>suggestedPersona</c> is well outside <c>CatalogIndexValidator.SlugSegment</c>'s
/// own vocabulary (spaces, uppercase, punctuation).</summary>
file static class InvalidSuggestedPersonaFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/bad-suggestion-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "bad-suggestion-pack";
    const string ItemName = "Classic";
    const string ItemFile = "classic.png";
    const string InvalidSuggestedPersona = "Not A Valid Slug!!";

    static string ManifestJson => $$"""
        {"packName":"Bad Suggestion","items":[
          {"name":"{{ItemName}}","file":"{{ItemFile}}","suggestedPersona":"{{InvalidSuggestedPersona}}"}
        ]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A malformed suggestedPersona hint.","audience":"everyone","added":"2026-08-15"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] itemBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{ItemFile}}", "sha256": "{{Sha256Hex(itemBytes)}}", "bytes": {{itemBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] itemBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(itemBytes),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + ItemFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(itemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioAnItemNameOutsideTheAllowedShapeRefuses</c>'s control-character-arm
/// Fact (review finding S2, round 2) — one item whose <c>name</c> carries a REAL tab control character
/// once parsed, reached via the JSON-ESCAPED <c>\t</c> form (two literal characters, backslash then
/// <c>t</c>, in the JSON TEXT itself). A JSON string literal can NOT embed a raw, unescaped control
/// character directly — RFC 8259 §7 forbids it, and System.Text.Json THROWS a
/// <see cref="System.Text.Json.JsonException"/> the instant it hits one (an earlier round of this
/// fixture claimed the opposite and injected a raw 0x09 byte via plain C# string concatenation; that
/// manifest never actually PARSED, so the Fact went green off <c>CatalogInstallShell.MalformedManifestProblem</c>'s
/// generic 400 and never reached <c>IsValidItemName</c> at all). The escaped <c>\t</c> form below is
/// what a real catalog author would write, and is the only way a control character legitimately reaches
/// <c>CatalogAvatarPackManifestSerializer.Deserialize</c> as a real, non-empty <c>Name</c> that
/// <c>IsValidItemName</c> must still catch.</summary>
file static class InvalidItemNameFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/bad-name-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "bad-name-pack";
    const string ItemFile = "classic.png";

    // \t here is the JSON escape sequence (two literal characters in this raw string literal — raw
    // string literals process no escapes of their own), so the SERVED bytes contain the text \t, which
    // System.Text.Json parses into a real 0x09 tab in the deserialized Name.
    static string ManifestJson => $$"""
        {"packName":"Bad Name","items":[{"name":"Bad\tName","file":"{{ItemFile}}"}]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"An item name carrying a control character.","audience":"everyone","added":"2026-08-15"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] itemBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{ItemFile}}", "sha256": "{{Sha256Hex(itemBytes)}}", "bytes": {{itemBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] itemBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(itemBytes),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + ItemFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(itemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioAnItemNameOutsideTheAllowedShapeRefuses</c>'s length-arm Fact (review
/// finding S2, round 2) — one item whose <c>name</c> is JSON-legal as-is (plain ASCII letters, no
/// escaping, no control character) but sits ONE character over <c>AvatarPackController</c>'s
/// 64-character item-name length cap, so this Fact reaches <c>IsValidItemName</c>'s LENGTH arm
/// specifically rather than its control-character arm (the sibling
/// <c>InvalidItemNameFixtures</c>).</summary>
file static class TooLongItemNameFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/too-long-name-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "too-long-name-pack";
    const string ItemFile = "classic.png";

    // 65 plain ASCII letters — one past AvatarPackController's 64-character item-name cap. No escaping
    // needed (unlike InvalidItemNameFixtures's control-character sibling), so this exercises the length
    // arm of IsValidItemName in isolation from its control-character arm.
    static readonly string ItemName = new('A', 65);

    static string ManifestJson => $$"""
        {"packName":"Too Long Name","items":[{"name":"{{ItemName}}","file":"{{ItemFile}}"}]}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"An item name over the length ceiling.","audience":"everyone","added":"2026-08-15"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson(byte[] itemBytes) => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{ItemFile}}", "sha256": "{{Sha256Hex(itemBytes)}}", "bytes": {{itemBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(byte[] itemBytes)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(itemBytes),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + ItemFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(itemBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Fixture for <c>ScenarioSixMebiByteCeilingCutsOffEarly</c> (review finding S8) — mirrors
/// FontPackController's own <c>PackByteCeilingFixtures</c> (Story282_FontPackInstall.cs), scaled to
/// the avatar kind's own 6 MiB pack ceiling and 512 KiB-per-asset transport cap
/// (<c>CatalogIndexValidator.MaxPngAssetBytes</c>): every filler asset must individually stay AT that
/// per-asset ceiling (unlike a font pack's much smaller world, one over-sized avatar asset can't carry
/// the whole overage alone), so THIRTEEN 512 KiB assets (6.5 MiB total) are declared before the
/// "successor" asset this fixture's own Facts assert was never requested.</summary>
file static class SixMebiByteCeilingFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/avatar-ceiling-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "huge-avatar-pack";

    // Never parsed as a real avatar manifest — the pack-bytes ceiling refuses inside the fetch loop,
    // strictly before CatalogAvatarPackManifestSerializer.Deserialize ever runs (mirrors
    // PackByteCeilingFixtures's own "{}" placeholder).
    const string ManifestJson = "{}";
    const string MetaJson = "{}";

    // 13 × 512 KiB = 6.5 MiB, over the 6 MiB AvatarPackController.MaxPackBytes ceiling; each
    // individually AT (never over) CatalogIndexValidator.MaxPngAssetBytes, so none is withheld as
    // Oversize on its own.
    const int OverflowingAssetCount = 13;
    static readonly byte[] FullAssetBytes = Filler(0xAA, CatalogIndexValidator.MaxPngAssetBytes);

    const string SuccessorFile = "successor.png";
    static readonly byte[] SuccessorBytes = Filler(0xFF, 16);

    public static string SuccessorAssetUrl => Directory + "entries/" + Slug + "/" + SuccessorFile;

    static byte[] Filler(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    static string FullAssetFile(int index) => $"item-{index}.png";

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string IndexJson()
    {
        var assetEntries = Enumerable.Range(0, OverflowingAssetCount)
            .Select(i => $$"""{ "path": "entries/{{Slug}}/{{FullAssetFile(i)}}", "sha256": "{{Sha256Hex(FullAssetBytes)}}", "bytes": {{FullAssetBytes.Length}} }""")
            .Append($$"""{ "path": "entries/{{Slug}}/{{SuccessorFile}}", "sha256": "{{Sha256Hex(SuccessorBytes)}}", "bytes": {{SuccessorBytes.Length}} }""");

        return $$"""
            { "generatedAt": "2026-08-15", "entries": [
              { "slug": "{{Slug}}", "kind": "avatar", "audience": "everyone",
                "manifest": { "path": "entries/{{Slug}}/{{Slug}}.avatar.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
                "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
                "assets": [ {{string.Join(",\n", assetEntries)}} ] } ] }
            """;
    }

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".avatar.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < OverflowingAssetCount; i++)
            assetBytesByUrl[Directory + "entries/" + Slug + "/" + FullAssetFile(i)] = FullAssetBytes;
        assetBytesByUrl[SuccessorAssetUrl] = SuccessorBytes;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
