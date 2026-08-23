// STORY-361 — The Announcements page's own history read (SPEC F146.2 · PLAN T344)
//
// BDD specification — xUnit. WIRED T344 — every Fact below drives the real production
// GET /api/announcements route (Operator plane, session OR announce-token auth — the same family
// AnnouncementsController.Post already serves, mirrors Story357_AnnouncementEndpoint.cs's own idiom)
// through WebApplicationFactory<Program>, against a FakeAnnouncementStore double — no live Postgres,
// this project has none for Host.Tests. The store's own HistoryAsync SQL (newest-first, every state)
// is proven for real against a real Postgres fixture by GenWave.MediaLibrary.Tests/Specs/
// Story357_AnnouncementStore.cs — this file never re-derives that SQL, only the WIRE: what
// AnnouncementsController asks the store for (the capped limit) and what it hands back (the DTO shape).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Auth;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementHistoryEndpoint
{
    public sealed class ScenarioEveryStateIsVisible
    {
        [Fact]
        public async Task TheDtoCarriesStateDeclineReasonCollapseCountAndAiredAt()
        {
            // Given a logged-in operator and one row of each SPEC F143.2 terminal state, with a
            // declined row's reason and an aired row's timestamp both populated...
            var store = new FakeAnnouncementStore
            {
                HistoryRows =
                [
                    new AnnouncementHistoryEntry(
                        5, "Declined one", false, "declined", "station went public", 1,
                        new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 8, 22, 10, 15, 0, DateTimeKind.Utc), null),
                    new AnnouncementHistoryEntry(
                        4, "Aired one", true, "aired", null, 3,
                        new DateTime(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 8, 22, 9, 15, 0, DateTimeKind.Utc),
                        new DateTime(2026, 8, 22, 9, 5, 0, DateTimeKind.Utc)),
                ],
            };
            await using var factory = new AnnouncementHistoryApiWebFactory(store);
            var client = await AnnouncementHistoryApiWebFactory.LoggedInClientAsync(factory);

            // When the history is read
            var rows = await client.GetFromJsonAsync<List<AnnouncementHistoryDto>>("/api/announcements");

            // Then every field the visible-decline surface promises rides the wire, per row
            Assert.NotNull(rows);
            var declined = rows.Single(r => r.Id == 5);
            var aired = rows.Single(r => r.Id == 4);
            Assert.Equal(
                (State: "declined", Reason: "station went public", Count: 1, Aired: (DateTime?)null),
                (State: declined.State, Reason: declined.DeclineReason, Count: declined.CollapseCount, Aired: declined.AiredAt));
            Assert.Equal(
                (State: "aired", Reason: (string?)null, Count: 3, Aired: (DateTime?)new DateTime(2026, 8, 22, 9, 5, 0, DateTimeKind.Utc)),
                (State: aired.State, Reason: aired.DeclineReason, Count: aired.CollapseCount, Aired: aired.AiredAt));
        }

        [Fact]
        public async Task TheAnnounceTokenAuthorizesTheHistoryReadToo()
        {
            // Given a station with NO cookie session at all, only a Bearer credential — mirrors
            // Story360_AnnounceToken.cs's own "same family, same schemes attribute" facts
            var store = new FakeAnnouncementStore { HistoryRows = [] };
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnouncementHistoryApiWebFactory(store, tokenStore);
            var loggedInClient = await AnnouncementHistoryApiWebFactory.LoggedInClientAsync(factory);
            var generate = await loggedInClient.PostAsync("/api/announcements/token", content: null);
            var plaintext = (await generate.Content.ReadFromJsonAsync<AnnounceTokenGeneratedDto>())!.Token;
            var bearerOnlyClient = factory.CreateClient();
            bearerOnlyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

            // When that Bearer-only client reads history
            var response = await bearerOnlyClient.GetAsync("/api/announcements");

            // Then it succeeds — GET /api/announcements shares AnnouncementsController's own
            // InScopeSchemes attribute, the same door POST and now-playing already accept
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public sealed class ScenarioTheLimitIsCappedAtTheEndpoint
    {
        [Fact]
        public async Task NoLimitQueryParamAsksTheStoreForFifty()
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore { HistoryRows = [] };
            await using var factory = new AnnouncementHistoryApiWebFactory(store);
            var client = await AnnouncementHistoryApiWebFactory.LoggedInClientAsync(factory);

            // When history is read with no ?limit
            await client.GetAsync("/api/announcements");

            // Then the store saw the 50-row default, never an unbounded read (T337 review's own
            // unbounded-limit carry-forward)
            Assert.Equal(50, Assert.Single(store.HistoryCalls));
        }

        [Fact]
        public async Task ARequestedLimitAboveTwoHundredClampsDownToTwoHundred()
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore { HistoryRows = [] };
            await using var factory = new AnnouncementHistoryApiWebFactory(store);
            var client = await AnnouncementHistoryApiWebFactory.LoggedInClientAsync(factory);

            // When history is read asking for 9999
            await client.GetAsync("/api/announcements?limit=9999");

            // Then the store saw exactly the 200-row ceiling, never the caller's raw value
            Assert.Equal(200, Assert.Single(store.HistoryCalls));
        }

        [Fact]
        public async Task ARequestedLimitInsideBoundsIsHonored()
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore { HistoryRows = [] };
            await using var factory = new AnnouncementHistoryApiWebFactory(store);
            var client = await AnnouncementHistoryApiWebFactory.LoggedInClientAsync(factory);

            // When history is read asking for 10 (inside 1-200)
            await client.GetAsync("/api/announcements?limit=10");

            // Then the store saw exactly 10 — a reasonable ask is passed through unchanged
            Assert.Equal(10, Assert.Single(store.HistoryCalls));
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story357_AnnouncementEndpoint.cs's own <c>AnnouncementsApiWebFactory</c> idiom, widened with an
/// optional <see cref="IAnnounceTokenStore"/> double for the one Fact above that also drives the
/// Bearer door.
/// </summary>
file sealed class AnnouncementHistoryApiWebFactory(FakeAnnouncementStore store, FakeAnnounceTokenStore? tokenStore = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story361-announcement-history";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IAnnouncementStore>();
            services.AddSingleton<IAnnouncementStore>(store);

            if (tokenStore is not null)
            {
                services.RemoveAll<IAnnounceTokenStore>();
                services.AddSingleton<IAnnounceTokenStore>(tokenStore);
            }
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
