// STORY-224 — Anyone can ask; nobody can probe (SPEC F87.1–F87.3, F87.8; PLAN T86–T87)
//
// BDD specification — xUnit. Every scenario drives POST /spectator/api/requests through the
// production pipeline (WebApplicationFactory) — SurfaceGateMiddleware, the dedicated
// cooldown+daily-cap rate limiter, and the controller's own contract are all real. A fake
// IRequestStore stands in for the station_svc Postgres connection (this codebase's first public
// anonymous WRITE endpoint gets no real-DB round trip here, same convention as Story195's
// FakeBoothLogReader); a recording IStationEventSink stands in for the booth-log queue so a
// scenario can assert the published narrative event without a database.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Brings up the real HTTP pipeline (routing, the surface gate, the requests rate limiter) with
/// <c>Station:SpectatorMode</c>/<c>Station:Requests:Enabled</c> live-settable per test and the
/// throttle knobs (<c>Requests:PerIpCooldownMinutes</c>/<c>PerIpDailyCap</c>/<c>PendingCap</c>)
/// overridable — <c>UseSetting</c> feeds <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>,
/// which every one of these binds from directly, no database row needed (mirrors Story167's
/// <c>SpectatorSettingWebFactory</c>/Story163's <c>PoliciesWebFactory</c> shape).
/// </summary>
file sealed class RequestIntakeWebFactory(
    bool requestsEnabled = true,
    int? perIpCooldownMinutes = null,
    int? perIpDailyCap = null,
    int? pendingCap = null) : WebApplicationFactory<Program>
{
    public FakeRequestStore RequestStore { get; } = new();
    public CapturingEventSink Events { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Requests:Enabled", requestsEnabled ? "true" : "false");
        if (perIpCooldownMinutes is { } cooldown)
            builder.UseSetting("Requests:PerIpCooldownMinutes", cooldown.ToString(CultureInfo.InvariantCulture));
        if (perIpDailyCap is { } dailyCap)
            builder.UseSetting("Requests:PerIpDailyCap", dailyCap.ToString(CultureInfo.InvariantCulture));
        if (pendingCap is { } cap)
            builder.UseSetting("Requests:PendingCap", cap.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IRequestStore>();
            services.AddSingleton<IRequestStore>(RequestStore);
            services.RemoveAll<IStationEventSink>();
            services.AddSingleton<IStationEventSink>(Events);
        });
    }
}

public static class FeatureRequestIntake
{
    const string ValidWish = "play some jazz please";
    const string Route = "/spectator/api/requests";

    public static class ScenarioConstantAcceptance
    {
        [Fact]
        public static async Task AnAcceptedWishReturns202()
        {
            await using var factory = new RequestIntakeWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { wish = ValidWish });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public static async Task TheBodyIsByteIdenticalForMatchableUnmatchableAndGibberishWishes()
        {
            // The request line is not a catalog oracle and not a request-state oracle (F87.1). Each
            // wish gets its OWN factory/client: an in-memory TestServer client has no resolvable
            // remote IP, so every request within one factory shares the limiter's single
            // no-remote-ip partition (RateLimiterPolicies' own documented, intentional behavior) —
            // a second POST in the same factory would 429 rather than reveal a second body to compare.
            static async Task<string> PostAsync(string wish)
            {
                await using var factory = new RequestIntakeWebFactory();
                var client = factory.CreateClient();
                var response = await client.PostAsJsonAsync(Route, new { wish });
                return await response.Content.ReadAsStringAsync();
            }

            var matchable = await PostAsync("Night Drive by The Waveforms");
            var unmatchable = await PostAsync("a song that will never exist in any catalog");
            var gibberish = await PostAsync("asdkjhqwlekjhasd;lkqwje");

            Assert.Equal(matchable, unmatchable);
            Assert.Equal(matchable, gibberish);
        }

        [Fact]
        public static async Task AnAcceptedWishCreatesAPendingRowWithWindowExpiry()
        {
            // expires_at = received_at + WindowMinutes (F87.1). Station:Requests:WindowMinutes
            // defaults to 15 (StationRequestsOptions), not overridden by this factory.
            await using var factory = new RequestIntakeWebFactory();
            var client = factory.CreateClient();
            var before = DateTimeOffset.UtcNow;

            var response = await client.PostAsJsonAsync(Route, new { wish = ValidWish });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var inserted = Assert.Single(factory.RequestStore.Inserted);
            Assert.Equal(ValidWish, inserted.Wish);
            Assert.InRange(
                inserted.ExpiresAt,
                before.AddMinutes(15) - TimeSpan.FromSeconds(10),
                before.AddMinutes(15) + TimeSpan.FromSeconds(10));
        }

        [Fact]
        public static async Task TheBoothLogRecordsRequestReceivedWithoutTheWishText()
        {
            // F87.8 — narrative visibility, zero listener text. RequestReceived structurally
            // carries nothing beyond StationEvent.OccurredAt, so there is no wish field to assert
            // absent — the type itself is the guarantee.
            await using var factory = new RequestIntakeWebFactory();
            var client = factory.CreateClient();

            await client.PostAsJsonAsync(Route, new { wish = ValidWish });

            var published = Assert.Single(factory.Events.Events);
            Assert.IsType<RequestReceived>(published);
        }
    }

    public static class SadPathFailClosed
    {
        [Fact]
        public static async Task DisabledRequestsMeansTheEndpointIsAStandard404()
        {
            // F87.2 — surface-off semantics; not a "requests closed" oracle. SpectatorMode stays ON
            // (the factory default) so this proves the Requests-specific kill switch, not merely
            // the whole spectator surface being off.
            await using var factory = new RequestIntakeWebFactory(requestsEnabled: false);
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { wish = ValidWish });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(factory.RequestStore.Inserted);
        }

        [Fact]
        public static async Task ACallerInsideTheCooldownGets429AndNoRow()
        {
            // Default Requests:PerIpCooldownMinutes is 5 and Requests:PerIpDailyCap is 20 — the
            // second rapid POST from the same (shared, no-remote-ip) TestServer partition trips the
            // cooldown window specifically, not the daily cap.
            await using var factory = new RequestIntakeWebFactory();
            var client = factory.CreateClient();

            var first = await client.PostAsJsonAsync(Route, new { wish = ValidWish });
            var second = await client.PostAsJsonAsync(Route, new { wish = "a second, different wish" });

            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Single(factory.RequestStore.Inserted);
        }

        [Fact]
        public static async Task ACallerOverTheDailyCapGets429AndNoRow()
        {
            // Cooldown disabled (0) so the daily cap — not the cooldown — is what trips the second
            // call; PerIpDailyCap set to 1 so a single prior accepted request already exhausts it.
            await using var factory = new RequestIntakeWebFactory(perIpCooldownMinutes: 0, perIpDailyCap: 1);
            var client = factory.CreateClient();

            var first = await client.PostAsJsonAsync(Route, new { wish = ValidWish });
            var second = await client.PostAsJsonAsync(Route, new { wish = "a second, different wish" });

            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Single(factory.RequestStore.Inserted);
        }

        [Fact]
        public static async Task AnOverLengthWishGets400AndNothingIsWritten()
        {
            // Default Requests:WishMaxLength is 140 (RequestsOptions).
            await using var factory = new RequestIntakeWebFactory();
            var client = factory.CreateClient();
            var overLength = new string('a', 141);

            var response = await client.PostAsJsonAsync(Route, new { wish = overLength });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(factory.RequestStore.Inserted);
        }

        [Fact]
        public static async Task AtThePendingCapTheOldestPendingRowIsEvicted()
        {
            // Station-wide PendingCap forced to 1 and the fake store scripted to already be AT that
            // cap (F87.3) — the accepted POST must evict the oldest pending row first, then insert.
            await using var factory = new RequestIntakeWebFactory(pendingCap: 1);
            factory.RequestStore.PendingCount = 1;
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { wish = ValidWish });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(1, factory.RequestStore.EvictionCalls);
            Assert.Single(factory.RequestStore.Inserted);
            Assert.Contains(factory.Events.Events, evt => evt is RequestEvicted);
        }
    }
}
