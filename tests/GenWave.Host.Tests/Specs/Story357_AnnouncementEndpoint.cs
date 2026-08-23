// STORY-357 — An accepted announcement never vanishes (SPEC F143.1/.4 · PLAN T339)
//
// BDD specification — xUnit. WIRED T339 — every Fact below drives the real production
// POST /api/announcements route (Operator plane, session/cookie auth) through
// WebApplicationFactory<Program> with real cookie auth (mirrors Story305_ShowsApi.cs's own idiom),
// against a FakeAnnouncementStore double — no live Postgres, this project has none for Host.Tests.
// The store's own collapse-aware insert (SPEC F143.5) and its 280-char CHECK backstop are proven for
// real, against a real Postgres fixture, in GenWave.MediaLibrary.Tests/Specs/Story357_AnnouncementStore.cs
// — this file never re-derives that SQL, only the WIRE: what AnnouncementsController sends the store,
// and what HTTP surface each of its own gates (message cap, ttl bounds, accepted-rate limiter, pending
// depth) produces. The station.announcement row itself, durable before the 2xx returns, is proven
// against a REAL Postgres by the manual wire-proof transcript (PLAN T339's own acceptance) — not by
// this in-process suite, which has no database to query.

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
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementEndpoint
{
    public sealed class ScenarioAcceptedMeansDurableBeforeTheReply
    {
        [Fact]
        public async Task AValidPostCreatesThePendingRowBeforeTheTwoHundredReturns()
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When a valid announcement posts with no overrides
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });

            // Then the store's own collapse-aware insert already ran — synchronously, on the SAME
            // request — before this 200 was ever returned: session-derived source, flavored
            // (verbatim omitted ⇒ false), and no ttl override (the store applies its own 900s default)
            Assert.Equal(
                (Status: HttpStatusCode.OK, InsertCalls: 1,
                 Call: ("Dinner's ready", Verbatim: false, RequestedVoice: (string?)null, AnnouncementSubmitter.Session, Ttl: (TimeSpan?)null)),
                (Status: response.StatusCode, InsertCalls: store.InsertCalls.Count, Call: store.InsertCalls.Single()));
        }

        [Fact]
        public async Task ATtlOverrideInsideTheBoundsIsHonored()
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When a ttlSeconds override inside 60–3600 posts
            var response = await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Trash goes out tonight", ttlSeconds = 120 });

            // Then it is honored verbatim on the store call, not silently discarded to the default
            Assert.Equal(
                (Status: HttpStatusCode.OK, Ttl: (TimeSpan?)TimeSpan.FromSeconds(120)),
                (Status: response.StatusCode, Ttl: store.InsertCalls.Single().Ttl));
        }

        [Theory]
        [InlineData(59)]
        [InlineData(3601)]
        public async Task ATtlOverrideOutsideSixtyToThirtySixHundredIsRejected(int ttlSeconds)
        {
            // Given a logged-in operator
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When a ttlSeconds override outside the SPEC F143.1 60–3600 bound posts
            var response = await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Out of bounds ttl", ttlSeconds });

            // Then it is a 400 and nothing was ever written
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, InsertCalls: 0),
                (Status: response.StatusCode, InsertCalls: store.InsertCalls.Count));
        }
    }

    public sealed class ScenarioEveryCapDeclinesVisibly
    {
        [Fact]
        public async Task AMessageOverTwoEightyCharsIsAFourHundredWithAnHonestReason()
        {
            // Given a logged-in operator and a 281-char message
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When it posts
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = new string('a', 281) });

            // Then 400, the reason names the 280-char cap, and nothing was written
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesTheCap: true, InsertCalls: 0),
                (Status: response.StatusCode, NamesTheCap: detail.Contains("280", StringComparison.Ordinal),
                 InsertCalls: store.InsertCalls.Count));
        }

        [Fact]
        public async Task AVoiceOverSixtyFourCharsIsAFourHundredWithAnHonestReason()
        {
            // Given a logged-in operator and a 65-char voice (T339 review finding F2)
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When it posts alongside an otherwise-valid message
            var response = await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Dinner's ready", voice = new string('v', 65) });

            // Then 400, the reason names the 64-char voice cap, and nothing was written
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesTheCap: true, InsertCalls: 0),
                (Status: response.StatusCode, NamesTheCap: detail.Contains("64", StringComparison.Ordinal),
                 InsertCalls: store.InsertCalls.Count));
        }

        [Fact]
        public async Task ASeventhAcceptedSubmissionInsideAMinuteIsAFourTwentyNine()
        {
            // Given a logged-in operator and the default 6/min station-wide ACCEPTED-rate budget
            // (T339 review finding F1: the cap now lives in-action, via
            // AnnouncementAcceptedRateLimiter, acquired only after every other gate — not a
            // rate-limiter middleware policy upstream of the controller)
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When seven otherwise-valid announcements post inside the same minute
            var responses = new List<HttpResponseMessage>();
            for (var i = 0; i < 7; i++)
            {
                responses.Add(await client.PostAsJsonAsync("/api/announcements", new { message = $"Announcement number {i}" }));
            }
            var statuses = responses.Select(r => r.StatusCode).ToList();
            var seventhDetail = await DetailAsync(responses[6]);

            // Then the first six are TRULY accepted (each one reached the store) and the seventh is
            // throttled with an honest, non-empty reason naming the per-minute cap (F4 — this fact was
            // dishonest under the old middleware shape, whose framework 429 carries no body at all)
            Assert.True(
                statuses.Take(6).All(s => s == HttpStatusCode.OK)
                    && statuses[6] == HttpStatusCode.TooManyRequests
                    && store.InsertCalls.Count == 6
                    && seventhDetail.Contains('6'),
                $"expected six accepted then an honestly-throttled seventh; statuses: {string.Join(",", statuses)}, insertCalls: {store.InsertCalls.Count}, seventhDetail: {seventhDetail}");
        }

        [Fact]
        public async Task SixRefusedRequestsNeverSpendTheAcceptedBudget()
        {
            // Given a station with the default 6/min accepted-rate budget and NO logged-in client yet
            // (T339 review finding F1/F4: the accepted-rate cap must count ACCEPTED submissions,
            // post-auth — an anonymous prober must never be able to drain the operator's own window)
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var anonymousClient = factory.CreateClient();

            // When six unauthenticated posts are refused (401, never reaching the action — let alone
            // the accepted-rate limiter — at all) before a seventh, now-logged-in, otherwise-valid post
            var refusedStatuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                var response = await anonymousClient.PostAsJsonAsync("/api/announcements", new { message = "Trying without a session" });
                refusedStatuses.Add(response.StatusCode);
            }
            var loggedInClient = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);
            var accepted = await loggedInClient.PostAsJsonAsync("/api/announcements", new { message = "First real submission" });

            // Then every refusal was a 401 and the first real submission still succeeds — a refused
            // request never spends a permit from the accepted-rate budget
            Assert.True(
                refusedStatuses.All(s => s == HttpStatusCode.Unauthorized) && accepted.StatusCode == HttpStatusCode.OK,
                $"expected six 401s then an accepted 200; refused: {string.Join(",", refusedStatuses)}, accepted: {accepted.StatusCode}");
        }

        [Fact]
        public async Task SixInActionRefusalsNeverSpendTheAcceptedBudget()
        {
            // Given a logged-in operator and the default 6/min accepted-rate budget (T339 review
            // finding F1 — this pins the acquire's POSITION, not just its post-auth reach: a
            // TryAcquire hoisted above the other in-action refusal gates would still be past auth and
            // so cannot be caught by this Fact's own 401-based sibling above)
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When six over-length posts are refused INSIDE the action (400, past auth, past
            // SpectatorMode — caught by the message-length gate) before a seventh, valid post
            var refusedStatuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                var response = await client.PostAsJsonAsync("/api/announcements", new { message = new string('a', 281) });
                refusedStatuses.Add(response.StatusCode);
            }
            var accepted = await client.PostAsJsonAsync("/api/announcements", new { message = "First real submission" });

            // Then every refusal was a 400 and the first real submission still succeeds — an in-action
            // refusal never spends a permit from the accepted-rate budget (mutation-proven at review:
            // hoisting TryAcquire above these gates turns this red)
            Assert.True(
                refusedStatuses.All(s => s == HttpStatusCode.BadRequest) && accepted.StatusCode == HttpStatusCode.OK,
                $"expected six 400s then an accepted 200; refused: {string.Join(",", refusedStatuses)}, accepted: {accepted.StatusCode}");
        }

        [Fact]
        public async Task AThirteenthPendingAnnouncementIsAFourTwentyNine()
        {
            // Given a station already sitting at the default pending-depth cap (12)
            var store = new FakeAnnouncementStore { PendingCount = 12 };
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When one more otherwise-valid announcement posts
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = "One too many" });

            // Then 429 with an honest reason, and nothing was written — no declined row either
            // (SPEC F143.4: the depth cap never even calls the store's insert)
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.TooManyRequests, NamesTheCap: true, InsertCalls: 0),
                (Status: response.StatusCode, NamesTheCap: detail.Contains("12", StringComparison.Ordinal),
                 InsertCalls: store.InsertCalls.Count));
        }

        [Fact]
        public async Task NoCappedRequestEverReachesTheStore()
        {
            // Given a station already at the pending-depth cap AND an over-length message ready to send
            var store = new FakeAnnouncementStore { PendingCount = 12 };
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When both a length-capped and a depth-capped request post
            var overLength = await client.PostAsJsonAsync("/api/announcements", new { message = new string('a', 281) });
            var atDepthCap = await client.PostAsJsonAsync("/api/announcements", new { message = "valid but station is full" });

            // Then both decline and the store's insert was never called for either
            Assert.Equal(
                (OverLength: HttpStatusCode.BadRequest, AtDepthCap: HttpStatusCode.TooManyRequests, InsertCalls: 0),
                (OverLength: overLength.StatusCode, AtDepthCap: atDepthCap.StatusCode, InsertCalls: store.InsertCalls.Count));
        }

        [Fact]
        public async Task TheStoreDeclineBackstopIsAFourHundredNeverAFiveHundred()
        {
            // Given a logged-in operator and a store scripted to decline its own insert — the T337
            // 280-char CHECK backstop (T339 review finding F3), reachable only if the store's own hard
            // limit and this action's configured MessageMaxChars ever diverge
            var store = new FakeAnnouncementStore { DeclineNextInsert = true };
            await using var factory = new AnnouncementsApiWebFactory(store);
            var client = await AnnouncementsApiWebFactory.LoggedInClientAsync(factory);

            // When an otherwise-valid announcement posts
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = "Fits the endpoint's own cap" });

            // Then 400 — never a raw 500 — with an honest reason naming the STORE's own 280-char hard
            // limit, distinct from the endpoint's configured MessageMaxChars
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesTheStoreCap: true),
                (Status: response.StatusCode, NamesTheStoreCap: detail.Contains("280", StringComparison.Ordinal)));
        }
    }

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story305_ShowsApi.cs's own <c>ShowsApiWebFactory</c> idiom: <see cref="IAnnouncementStore"/>
/// replaced by a stateful fake, real cookie auth, no live Postgres/Liquidsoap/Kokoro reached.
/// </summary>
file sealed class AnnouncementsApiWebFactory(FakeAnnouncementStore store)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story357-announcements";

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
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story305_ShowsApi.cs's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
