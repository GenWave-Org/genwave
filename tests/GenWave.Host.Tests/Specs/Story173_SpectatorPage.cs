// STORY-173 — Spectator page: single static pane served by the host binary
//
// BDD specification — xUnit (SPEC F63.1–F63.5). The page is a static bundle in
// GenWave.Host/wwwroot/spectator — no Node runtime. These specs cover the server-observable
// contract: it serves, it is HTML, its assets reference only /spectator/api/*, and styling is
// token-driven. Client-rendered behavior (progress bar, standby card, hidden player) is
// browser-verified in PLAN T16 against the running compose stack — marked skipped here so the
// contract stays enumerated in one place. Red until PLAN T16.

using System.Net;
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

file sealed class SpectatorPageWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLib = Environment.GetEnvironmentVariable("ConnectionStrings__Library");
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", "test-password-x7z");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Library", prevLib);
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

public static class FeatureSpectatorPage
{
    const string BrowserGated =
        "Client-rendered behavior — verified in a real browser against the compose stack (PLAN T16 acceptance).";

    /// <summary>The page plus every same-origin asset it references (src/href), as raw text.</summary>
    static async Task<IReadOnlyList<(string Path, string Content)>> FetchPageBundleAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/spectator");
        var bundle = new List<(string, string)> { ("/spectator", html) };

        // Resolve each reference against the REAL document URL (RFC 3986), exactly as a browser
        // would: base = /spectator with NO trailing slash, so a bare relative reference like
        // "styles.css" resolves against the URL's parent ("/styles.css") rather than
        // "/spectator/styles.css" — the exact bug class this catches (a relative href/src 404s in
        // a real browser; only an absolute "/spectator/..." reference survives unchanged).
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

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioPageServesStatically
    {
        [Fact]
        public async Task PageIsServedWithoutCredentials()
        {
            await using var factory = new SpectatorPageWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PageIsHtml()
        {
            await using var factory = new SpectatorPageWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioSpectatorApiOnly
    {
        [Fact]
        public async Task NoAssetReferencesTheAdminApi()
        {
            await using var factory = new SpectatorPageWebFactory();
            var client = factory.CreateClient();

            var bundle = await FetchPageBundleAsync(client);

            Assert.All(bundle, item =>
            {
                // Any "/api/..." reference must be "/spectator/api/..." — never the admin surface.
                var adminCalls = Regex.Matches(item.Content, @"""/api/")
                    .Cast<Match>().ToList();
                Assert.True(adminCalls.Count == 0,
                    $"{item.Path} references the admin API surface (F63.2).");
            });
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioWirelessTokensOnly
    {
        [Fact]
        public async Task StylingDefinesCssCustomPropertyTokens()
        {
            await using var factory = new SpectatorPageWebFactory();
            var client = factory.CreateClient();

            var bundle = await FetchPageBundleAsync(client);

            Assert.Contains(bundle, item =>
                item.Content.Contains(":root", StringComparison.Ordinal) &&
                item.Content.Contains("--", StringComparison.Ordinal));
        }
    }

    // ── SAD PATH / client-rendered contract (browser-gated, PLAN T16) ─────

    public sealed class ScenarioClientRenderedContract
    {
        [Fact(Skip = BrowserGated)]
        public void ProgressBarRendersWhenDurationMsPresent() { }

        [Fact(Skip = BrowserGated)]
        public void ElapsedOnlyRendersWhenDurationMsNull() { }

        [Fact(Skip = BrowserGated)]
        public void StandbyStateRendersStationIdentificationCardNotAnError() { }

        [Fact(Skip = BrowserGated)]
        public void AudioPlayerHiddenWithConfigurationHintWhenStreamUrlEmpty() { }
    }
}
