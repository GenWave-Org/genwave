// STORY-359 — The house never leaks to a public stream (SPEC F145.1/.2 · PLAN T339 + T343)
//
// BDD specification — xUnit. ScenarioTheEndpointRefusesWhilePublic's two Facts are WIRED T339 — they
// drive the real production POST /api/announcements route through WebApplicationFactory<Program>
// with Station:SpectatorMode forced on, the same idiom Story357_AnnouncementEndpoint.cs uses.
// ScenarioGoingPublicDeclinesTheQueue stays Skip-tagged for PLAN T343 (the private→public transition
// guardian doesn't exist yet) — this file only fills in the endpoint's own half of F145.1/.2.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementPrivacy
{
    public sealed class ScenarioTheEndpointRefusesWhilePublic
    {
        [Fact]
        public async Task AValidPostUnderSpectatorModeIsAFourOhThreeWithAnHonestReason()
        {
            // Given a logged-in operator on a station with Station:SpectatorMode on
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementPrivacyWebFactory(store);
            var client = await AnnouncementPrivacyWebFactory.LoggedInClientAsync(factory);

            // When an otherwise-valid announcement posts
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });

            // Then 403 with an honest reason naming the public-station cause
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(
                (Status: HttpStatusCode.Forbidden, NamesTheReason: true),
                (Status: response.StatusCode,
                 NamesTheReason: body.Contains("public", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task NoRowIsCreatedByTheRefusedPost()
        {
            // Given a logged-in operator on a station with Station:SpectatorMode on
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementPrivacyWebFactory(store);
            var client = await AnnouncementPrivacyWebFactory.LoggedInClientAsync(factory);

            // When an otherwise-valid announcement posts
            await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });

            // Then the store's insert was never called — structurally impossible, not merely refused
            // after the fact (F145.1)
            Assert.Empty(store.InsertCalls);
        }
    }

    public sealed class ScenarioGoingPublicDeclinesTheQueue
    {
        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void EveryPendingAnnouncementDeclinesAtThePrivateToPublicFlip() { }

        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void EveryClaimedAnnouncementDeclinesAtThePrivateToPublicFlip() { }

        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void TheDeclineReasonSaysTheStationWentPublic() { }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own two T339-tagged Facts — mirrors
/// Story357_AnnouncementEndpoint.cs's own <c>AnnouncementsApiWebFactory</c> idiom exactly, plus forcing
/// <c>Station:SpectatorMode</c> on (SPEC F145.1's own live-read seam, <c>SurfaceGateMiddleware</c>'s
/// sibling check inside <c>AnnouncementsController</c> itself — see that class's own remarks).
/// </summary>
file sealed class AnnouncementPrivacyWebFactory(FakeAnnouncementStore store) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story359-privacy";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:SpectatorMode", "true");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IAnnouncementStore>();
            services.AddSingleton<IAnnouncementStore>(store);
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip. Mirrors Story357_AnnouncementEndpoint.cs's own helper.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
