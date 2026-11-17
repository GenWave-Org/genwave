// STORY-551 — The callback count, the Status api half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. GET /api/status through the test host with a fake repository; the page half is admin-ui/__specs__/dashboard-callbacks-number.spec.tsx.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheCallbackCount
{
    static async Task<JsonElement> StatusAsync(ICallbackRateReader reader)
    {
        await using var factory = new CallbackStatusWebFactory(reader);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = CallbackStatusWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var response = await client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    // AC2 — the Status page
    public sealed class ScenarioTheStatusResponse
    {
        // Given 12 used over 16 DJ-hours

        [Fact]
        public async Task TheStatusResponseCarriesCallbacksPerDJHour075()
        {
            var body = await StatusAsync(new FakeCallbackRateReader(new CallbackRate(Asks: 20, Used: 12, Hours: 16)));

            Assert.Equal(0.75m, body.GetProperty("callbacks").GetProperty("perDjHour").GetDecimal());
        }
    }

    // T743 — the line under the number
    public sealed class ScenarioTheCountsBehindTheNumber
    {
        // Given 12 used out of 20 asked

        [Fact]
        public async Task TheStatusResponseCarriesAsksAndUsed()
        {
            var body = await StatusAsync(new FakeCallbackRateReader(new CallbackRate(Asks: 20, Used: 12, Hours: 16)));

            var callbacks = body.GetProperty("callbacks");
            Assert.Equal(20, callbacks.GetProperty("asks").GetInt32());
            Assert.Equal(12, callbacks.GetProperty("used").GetInt32());
        }
    }

    // AC3 — nothing yet (sad path)
    public sealed class ScenarioNothingYet
    {
        // Given no offers

        [Fact]
        public async Task TheStatusResponseCarriesNoCallbackRate()
        {
            var body = await StatusAsync(new FakeCallbackRateReader(new CallbackRate(Asks: 0, Used: 0, Hours: 16)));

            Assert.Equal(JsonValueKind.Null, body.GetProperty("callbacks").GetProperty("perDjHour").ValueKind);
        }

        [Fact]
        public void OffersWithNoDjHoursAlsoHaveNoRate() =>
            Assert.Null(new CallbackRate(Asks: 3, Used: 0, Hours: 0).PerDjHour);
    }

    // Fail-soft: the number is a side panel, like Repeats.
    public sealed class ScenarioAnUnreachableStore
    {
        [Fact]
        public async Task LeavesTheRestOfTheStatusIntact()
        {
            var body = await StatusAsync(new UnavailableCallbackRateReader());

            Assert.Equal(JsonValueKind.Null, body.GetProperty("callbacks").ValueKind);
            Assert.True(body.TryGetProperty("catalog", out _));
        }
    }
}

file sealed class UnavailableCallbackRateReader : ICallbackRateReader
{
    public Task<CallbackRate?> ReadAsync(CancellationToken ct) => Task.FromResult<CallbackRate?>(null);
}

/// <summary>The deployed pipeline with every Postgres-backed status dependency faked, as Story542's factory does.</summary>
file sealed class CallbackStatusWebFactory(ICallbackRateReader reader) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IRepeatNumbersReader>();
            services.AddSingleton<IRepeatNumbersReader>(new FakeRepeatNumbersReader());
            services.RemoveAll<ICallbackRateReader>();
            services.AddSingleton(reader);
        });
    }
}
