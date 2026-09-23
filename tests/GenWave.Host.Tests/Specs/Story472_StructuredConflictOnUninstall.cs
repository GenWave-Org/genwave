// STORY-472 — Structured conflict on uninstall (SPEC F204.3 · PLAN T563)
//
// AC8 drives the real production DELETE /api/fonts/{slug} route (FontPackController.Uninstall)
// through WebApplicationFactory<Program> against a FakeFontPackStore — the Story288_FontPackUninstall.cs
// idiom (write straight to the fake store, script FakeFontPackStore.ReferencingThemeSlugs) — and reads
// the 409 body's own top-level "referencedBy" property (ProblemDetails.Extensions entries flatten to
// top-level JSON siblings of status/title/detail, the LibrariesController dependentMediaCount/
// ScheduleController cellErrors precedent, never nested under a literal "extensions" member).
//
// AC9 proves the SAME "referencedBy is present and non-empty" claim across every one of the four
// controllers that can actually 409 on uninstall (Font/Voice/Jingle/AdPack — Icon and Avatar are
// guard-free by design, SPEC F128.3/F128.5, F130.5, and get their own 204-only fact below instead), one
// Fact per kind rather than a single dictionary assertion: the DB-backed kinds' own already-seeded
// arcs (VoicePackUninstallArc, JinglePackUninstallArc, AdPackUninstallGuardArc, all from
// Story401_PackUninstallGuards.cs/Story416_AdPackUninstallGuarded.cs) each live in their OWN named
// xUnit collection, and a single test class can only ever declare one [Collection(...)] — there is no
// way to inject three differently-collection-scoped fixtures into one constructor without re-running
// their own expensive Postgres-backed seeding, which the brief explicitly rules out ("don't reinvent
// the seeding").

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStructuredConflictOnUninstall
{
    public sealed class ScenarioDeleteAFontAThemeReferences
    {
        [Fact]
        public async Task NamesTheReferrer()
        {
            // Given an installed font pack two saved themes still reference (AC8's own "theme
            // names" — Font's own referencedBy carries theme SLUGS, not display names; see
            // FontPackController.ReferencedProblem's own remarks for why the store has nothing
            // else to name a theme by),
            var store = FakeFontPackStore.WithInstalledFace(
                Story472FontFixtures.Slug, Story472FontFixtures.Family, Story472FontFixtures.AssetFile,
                Story472FontFixtures.AssetBytes, Story472FontFixtures.AssetSha256);
            store.ReferencingThemeSlugs = Story472FontFixtures.ReferencingThemeSlugs;
            await using var factory = Story472PackWebFactory.WithFontStore(store);
            var client = await Story472PackWebFactory.LoggedInClientAsync(factory);

            // When the referenced font is uninstalled,
            var response = await client.DeleteAsync($"/api/fonts/{Story472FontFixtures.Slug}");

            // Then the 409's own referencedBy is EXACTLY the two referencing theme slugs, in order —
            // the one assertion this Fact needs (a missing/empty/mis-ordered referencedBy, or a
            // response that never refused at all, all fail this same equality).
            Assert.Equal(Story472FontFixtures.ReferencingThemeSlugs, await ReferencedByAsync(response));
        }
    }

    // ── AC9 — every refusing kind's own 409 carries a non-empty referencedBy ──────────────────────

    public sealed class ScenarioFontKindCarriesReferencedBy
    {
        [Fact]
        public async Task ReferencedByIsPresentAndNonEmpty()
        {
            // Given the same referenced-font arrangement as AC8, a fresh store/factory instance
            // (WebApplicationFactory is single-use),
            var store = FakeFontPackStore.WithInstalledFace(
                Story472FontFixtures.Slug, Story472FontFixtures.Family, Story472FontFixtures.AssetFile,
                Story472FontFixtures.AssetBytes, Story472FontFixtures.AssetSha256);
            store.ReferencingThemeSlugs = Story472FontFixtures.ReferencingThemeSlugs;
            await using var factory = Story472PackWebFactory.WithFontStore(store);
            var client = await Story472PackWebFactory.LoggedInClientAsync(factory);

            // When/Then — the DELETE 409 carries a non-empty referencedBy.
            var response = await client.DeleteAsync($"/api/fonts/{Story472FontFixtures.Slug}");
            Assert.NotEmpty(await ReferencedByAsync(response));
        }
    }

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioVoiceKindCarriesReferencedBy(VoicePackUninstallArc arc)
    {
        [Fact]
        public void ReferencedByIsPresentAndNonEmpty() =>
            // Given/When: Story401_PackUninstallGuards.cs's own VoicePackUninstallArc already drove
            // a real DELETE against a pack a persona references (PersonaGuardDeleteBody) — reused
            // verbatim, never re-seeded.
            Assert.NotEmpty(ReferencedByFromBody(arc.PersonaGuardDeleteBody));
    }

    [Collection(JinglePackUninstallCollection.Name)]
    public sealed class ScenarioJingleKindCarriesReferencedBy(JinglePackUninstallArc arc)
    {
        [Fact]
        public void ReferencedByIsPresentAndNonEmpty() =>
            // Given/When: JinglePackUninstallArc's own already-driven DELETE against a pack an
            // active ad spot's bed_media_id references (BedGuardDeleteBody).
            Assert.NotEmpty(ReferencedByFromBody(arc.BedGuardDeleteBody));
    }

    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioAdPackKindCarriesReferencedBy(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void ReferencedByIsPresentAndNonEmpty() =>
            // Given/When: AdPackUninstallGuardArc's own already-driven DELETE against a pack an
            // owner spot AND an owner show both reference (BothGuardDeleteBody).
            Assert.NotEmpty(ReferencedByFromBody(arc.BothGuardDeleteBody));
    }

    // ── F204.5's other half — Icon and Avatar uninstall are GUARD-FREE BY DESIGN (SPEC F128.3/
    //    F128.5, F130.5): DELETE on an installed pack of either kind 204s, never 409s, because
    //    neither has a referrer path at all. One extra fact keeps "all six DELETE routes" honest. ──

    public sealed class ScenarioIconAndAvatarUninstallNeverGuard
    {
        [Fact]
        public async Task BothDeleteAsNoContent()
        {
            // Given one installed icon pack and one installed avatar pack (seeded straight into the
            // fakes — IconPackController.Uninstall/AvatarPackController.Uninstall never gate on the
            // Community Catalog kill switch either, so no catalog fixture is needed),
            var iconStore = new FakeIconPackStore();
            await iconStore.UpsertAsync(Story472IconAvatarFixtures.IconSlug, "{}", Story472IconAvatarFixtures.IconSlug, CancellationToken.None);
            var avatarStore = new FakeAvatarPackStore();
            await avatarStore.UpsertAsync(
                Story472IconAvatarFixtures.AvatarSlug, "{}", Story472IconAvatarFixtures.AvatarSlug, [], CancellationToken.None);
            await using var factory = Story472PackWebFactory.WithIconAndAvatarStores(iconStore, avatarStore);
            var client = await Story472PackWebFactory.LoggedInClientAsync(factory);

            // When both are uninstalled through their own real DELETE routes,
            var iconDelete = await client.DeleteAsync($"/api/icon-packs/{Story472IconAvatarFixtures.IconSlug}");
            var avatarDelete = await client.DeleteAsync($"/api/avatar-packs/{Story472IconAvatarFixtures.AvatarSlug}");

            // Then both answer 204 — never a 409, since neither kind has a referrer path to guard.
            Assert.Equal(
                (Icon: HttpStatusCode.NoContent, Avatar: HttpStatusCode.NoContent),
                (Icon: iconDelete.StatusCode, Avatar: avatarDelete.StatusCode));
        }
    }

    // ── Shared JSON-body helpers ─────────────────────────────────────────────────────────────────

    static async Task<string[]> ReferencedByAsync(HttpResponseMessage response) =>
        ReferencedByFromBody(await response.Content.ReadAsStringAsync());

    static string[] ReferencedByFromBody(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("referencedBy").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
    }
}

// ── Test harness — a fake-store-backed WebApplicationFactory shared by every Fact above that does
//    NOT need a real ephemeral Postgres (Font, Icon, Avatar). Mirrors
//    Story288_FontPackUninstall.cs's own FontPackUninstallWebFactory idiom, widened to swap in
//    whichever of the three fake stores a given Fact actually needs — the other two default to an
//    empty fake, never touched by that Fact's own route. ─────────────────────────────────────────

file sealed class Story472PackWebFactory(FakeFontPackStore fontStore, FakeIconPackStore iconStore, FakeAvatarPackStore avatarStore)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story472-referencedby";

    public static Story472PackWebFactory WithFontStore(FakeFontPackStore fontStore) =>
        new(fontStore, new FakeIconPackStore(), new FakeAvatarPackStore());

    public static Story472PackWebFactory WithIconAndAvatarStores(FakeIconPackStore iconStore, FakeAvatarPackStore avatarStore) =>
        new(new FakeFontPackStore(), iconStore, avatarStore);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", "");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(fontStore);

            services.RemoveAll<IIconPackStore>();
            services.AddSingleton<IIconPackStore>(iconStore);

            services.RemoveAll<IAvatarPackStore>();
            services.AddSingleton<IAvatarPackStore>(avatarStore);
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

file static class Story472FontFixtures
{
    public const string Slug = "story472-referenced-pack";
    public const string Family = "Story472 Referenced";
    public const string AssetFile = "story472-referenced-variable-latin.woff2";

    public static readonly byte[] AssetBytes = "installed face bytes for the T563 referencedBy specs"u8.ToArray();

    public static string AssetSha256 => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(AssetBytes));

    public static readonly IReadOnlyList<string> ReferencingThemeSlugs = ["midnight-drive", "sunday-static"];
}

file static class Story472IconAvatarFixtures
{
    public const string IconSlug = "story472-icon-pack";
    public const string AvatarSlug = "story472-avatar-pack";
}
