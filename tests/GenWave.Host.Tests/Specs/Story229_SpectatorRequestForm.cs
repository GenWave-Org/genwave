// STORY-229 — The spectator page grows a request line (SPEC F87.11, PLAN T92)
//
// BDD specification — xUnit. The page is api-served static (F63) — served-markup facts here; the
// hide/show and 429 handling are client JS, proven in a real browser against the running stack
// (T92's acceptance line). Plan-time decision: the page learns the toggle from a
// `requestsEnabled` field on the about projection (pinned contract addition, re-pinned in
// Story183_DisclosureContractCompleteness.cs).

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorRequestFormWebFactory(bool requestsEnabled) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Requests:Enabled", requestsEnabled ? "true" : "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureSpectatorRequestForm
{
    /// <summary>The page plus every same-origin asset it references (src/href), as raw text — the
    /// exact idiom Story173_SpectatorPage.cs uses to fetch the served bundle.</summary>
    static async Task<IReadOnlyList<(string Path, string Content)>> FetchPageBundleAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/spectator");
        var bundle = new List<(string, string)> { ("/spectator", html) };
        var documentUri = new Uri(client.BaseAddress!, "/spectator");

        foreach (Match match in Regex.Matches(html, @"(?:src|href)\s*=\s*""([^""]+)"""))
        {
            var reference = match.Groups[1].Value;
            if (reference.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            var path = new Uri(documentUri, reference).AbsolutePath;
            var asset = await client.GetAsync(path);
            if (asset.IsSuccessStatusCode)
                bundle.Add((path, await asset.Content.ReadAsStringAsync()));
        }
        return bundle;
    }

    static async Task<JsonElement> FetchAboutAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/about");
        Assert.True(response.IsSuccessStatusCode, $"/spectator/api/about returned {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public static class ScenarioFormServed
    {
        [Fact]
        public static async Task ThePageBundleContainsTheWishFormWithClientSideLengthCap()
        {
            await using var factory = new SpectatorRequestFormWebFactory(requestsEnabled: true);
            var client = factory.CreateClient();

            var bundle = await FetchPageBundleAsync(client);

            Assert.Contains(bundle, item =>
                item.Content.Contains("id=\"request-form\"", StringComparison.Ordinal) &&
                item.Content.Contains("maxlength=\"140\"", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task TheAboutProjectionCarriesRequestsEnabled()
        {
            // The one new pinned public field this epic adds (F87.11 mechanism).
            await using var factory = new SpectatorRequestFormWebFactory(requestsEnabled: true);

            var body = await FetchAboutAsync(factory);

            Assert.True(body.GetProperty("requestsEnabled").GetBoolean());
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public static class SadPathQuietWhenOff
    {
        [Fact]
        public static async Task RequestsDisabledMeansAboutSaysSoAndTheFormLogicHidesIt()
        {
            // Served JS keys visibility off requestsEnabled — browser half verified at T92.
            await using var factory = new SpectatorRequestFormWebFactory(requestsEnabled: false);

            var body = await FetchAboutAsync(factory);

            Assert.False(body.GetProperty("requestsEnabled").GetBoolean());
        }
    }
}
