// STORY-238 — The shelf cannot touch the air (SPEC F90.8, PLAN T106)
//
// BDD specification — xUnit. Structural isolation pins: the catalog surface is
// admin-plane only, and its absence/failure is invisible everywhere else. Entry-point
// discipline: spectator surface probed on the public listener, catalog endpoints on the
// admin surface, both via WebApplicationFactory<Program>.

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.Playout;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;

using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

// ── File-scoped fakes/factories ──────────────────────────────────────────────────────────────

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> double (mirrors Story234's own copy — a
/// file-scoped type cannot cross files, so every spec file with this need defines its own).</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Yields exactly one item, then nothing — mirrors Story066's own <c>SingleItemProvider</c>.</summary>
file sealed class SingleTrackProvider(MediaItem item) : INextItemProvider
{
    bool yielded;

    public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        if (yielded) return Task.FromResult<MediaItem?>(null);
        yielded = true;
        return Task.FromResult<MediaItem?>(item);
    }
}

/// <summary>Stamps every request's Connection.LocalPort so the SurfaceGate sees the public
/// listener (mirrors Story172's own <c>SimulatedPortStartupFilter</c> — file-scoped, so this file
/// needs its own copy).</summary>
file sealed class CatalogPortStartupFilter(int port) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = port;
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>
/// Single shared factory for every fact in this file EXCEPT the playout-closure one (which
/// deliberately needs the REAL <c>IMediaCatalog</c>/<c>IActivePersonaAccessor</c> graph, not fakes
/// — see <see cref="PlayoutClosureWebFactory"/>). <paramref name="catalogIndexUrl"/> null leaves
/// <c>Community:CatalogIndexUrl</c> entirely unset (the shipped default — the official catalog
/// origin, i.e. ENABLED, not a "pre-F90 off" state — see <see cref="CommunityOptions.CatalogIndexUrl"/>'s
/// own default); <paramref name="spectatorMode"/>/<paramref name="publicPort"/> are only needed by
/// the two spectator/public-listener facts.
/// </summary>
file sealed class ShelfCannotTouchAirWebFactory(
    string? catalogIndexUrl = null,
    bool spectatorMode = false,
    int? publicPort = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story238";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        if (spectatorMode)
            builder.UseSetting("Station:SpectatorMode", "true");
        if (publicPort is int port)
            builder.UseSetting("Spectator:PublicPort", port.ToString());
        if (catalogIndexUrl is not null)
            builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            if (publicPort is int p)
                services.AddSingleton<IStartupFilter>(new CatalogPortStartupFilter(p));
        });
    }
}

/// <summary>
/// Boots the REAL <c>AddGenWavePlayout</c> graph — <c>IMediaCatalog</c>/<c>IActivePersonaAccessor</c>
/// and everything else left exactly as Program.cs wires them, deliberately NOT faked — with
/// <c>Community:CatalogIndexUrl</c> pointed at an unreachable origin. Only <c>IHostedService</c> is
/// removed, so <c>PlayoutSupervisor</c> never actually starts (no live Liquidsoap connection in this
/// test process); every registration <see cref="UnreachableCatalogLeavesPlayoutTicksUntouched"/>'s
/// closure walk inspects is the one Program.cs really ships.
/// </summary>
file sealed class PlayoutClosureWebFactory(string catalogIndexUrl) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-story238-closure");
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

public static class FeatureShelfCannotTouchAir
{
    public sealed class ScenarioByteIdenticalWithoutTheCatalog
    {
        // Given the catalog disabled (empty URL) and separately an unreachable origin.

        [Fact]
        public async Task SpectatorDisclosurePayloadsGainNoNewFields()
        {
            // Story183_DisclosureContractCompleteness.cs (F67.6) reflects a HAND-BUILT
            // SpectatorAbout/SpectatorStats instance — it never drives live HTTP, so it is blind to
            // GetAbout()/GetStats() drifting away from those DTOs entirely (e.g. returning an
            // anonymous object with an extra constant field bolted on: same shape on every request,
            // never touches Story183 at all). A byte-for-byte cross-config diff alone has the same
            // blind spot — an extra field baked in IDENTICALLY regardless of
            // Community:CatalogIndexUrl stays byte-equal across all three configs below and would
            // never surface. So this fact anchors BOTH ends: the live wire body is diffed against
            // the REFLECTED property set of the real SpectatorAbout/SpectatorStats TYPES
            // (AssertBodyMatchesDtoShape), closing live-body <-> DTO <-> Story183's blessed table —
            // and separately across the three catalog states (disabled / enabled-official-origin /
            // enabled-but-unreachable) so a config-driven field leak is caught too.
            var disabled = await FetchSpectatorPayloadsAsync(catalogIndexUrl: "");
            var enabledOfficialOrigin = await FetchSpectatorPayloadsAsync(catalogIndexUrl: null);
            var enabledUnreachable = await FetchSpectatorPayloadsAsync(
                catalogIndexUrl: "https://catalog.test/unreachable/index.json");

            AssertBodyMatchesDtoShape<SpectatorAbout>(disabled.About);
            AssertBodyMatchesDtoShape<SpectatorStats>(disabled.Stats);

            Assert.Equal(disabled.About, enabledOfficialOrigin.About);
            Assert.Equal(disabled.About, enabledUnreachable.About);
            Assert.Equal(disabled.Stats, enabledOfficialOrigin.Stats);
            Assert.Equal(disabled.Stats, enabledUnreachable.Stats);
        }

        static async Task<(string About, string Stats)> FetchSpectatorPayloadsAsync(string? catalogIndexUrl)
        {
            await using var factory = new ShelfCannotTouchAirWebFactory(catalogIndexUrl, spectatorMode: true);
            var client = factory.CreateClient();

            var about = await (await client.GetAsync("/spectator/api/about")).Content.ReadAsStringAsync();
            var stats = await (await client.GetAsync("/spectator/api/stats")).Content.ReadAsStringAsync();
            return (about, stats);
        }

        /// <summary>Diffs <paramref name="json"/>'s top-level property-name SET against
        /// <typeparamref name="TDto"/>'s own declared public properties (camelCase-converted, matching
        /// ASP.NET Core's default serialization) — a live-wire-to-DTO-shape anchor independent of
        /// Story183's own hand-built-instance check.</summary>
        static void AssertBodyMatchesDtoShape<TDto>(string json)
        {
            using var document = JsonDocument.Parse(json);
            var actual = document.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var declared = typeof(TDto).GetProperties()
                .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(declared, actual);
        }

        [Fact]
        public async Task PublicListenerExposesNoCatalogRoute()
        {
            // "Regardless of catalog enabled/disabled" (F90.8) — loop over both states and, for
            // each, prove GET /api/catalog/index and /api/catalog/entries/{slug} are indistinguishable
            // from a route this app never mapped at all (same idiom as Story172's own
            // ScenarioEverythingElseDoesNotExistPublicly): if the catalog surface leaked a body or a
            // different status on the public listener, this diffs against the genuinely-unmapped
            // control route and fails.
            const int publicPort = 8093;

            foreach (var catalogIndexUrl in new[] { "", "https://catalog.test/reachable-shaped/index.json" })
            {
                await using var factory = new ShelfCannotTouchAirWebFactory(
                    catalogIndexUrl, spectatorMode: true, publicPort: publicPort);
                var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

                var unmapped = await client.GetAsync("/api/catalog/definitely-not-a-real-route");
                Assert.Equal(HttpStatusCode.NotFound, unmapped.StatusCode); // control: genuinely unmapped
                var unmappedBody = await unmapped.Content.ReadAsStringAsync();

                var index = await client.GetAsync("/api/catalog/index");
                Assert.Equal(unmapped.StatusCode, index.StatusCode);
                Assert.Equal(unmappedBody, await index.Content.ReadAsStringAsync());

                var entry = await client.GetAsync("/api/catalog/entries/valid-dj");
                Assert.Equal(unmapped.StatusCode, entry.StatusCode);
                Assert.Equal(unmappedBody, await entry.Content.ReadAsStringAsync());
            }
        }

        [Fact]
        public async Task UnreachableCatalogLeavesPlayoutTicksUntouched()
        {
            // Reference-direction check (kept as a documentation-grade pin, NOT the load-bearing
            // assertion): GenWave.Core's/GenWave.Orchestration's own .csproj files reference only
            // each other/GenWave.Abstractions, never GenWave.Host. This can never go RED on its own
            // while the solution still compiles — a ProjectReference edit that violated it would be a
            // BUILD break, not a test failure, so it catches nothing at test time by itself. It is
            // NOT a substitute for the closure walk below: PlayoutFeederService/PlayoutSupervisor
            // (the actual production tick path) live in GenWave.Host, the SAME assembly as
            // CommunityCatalogAccessor/CatalogProxyService — a same-assembly reference there is
            // structurally invisible to an assembly-reference check no matter what it wires.
            var playoutReferences = typeof(PlayoutFeeder).Assembly.GetReferencedAssemblies().Select(a => a.Name);
            var orchestrationReferences = typeof(Orchestrator).Assembly.GetReferencedAssemblies().Select(a => a.Name);
            Assert.DoesNotContain("GenWave.Host", playoutReferences);
            Assert.DoesNotContain("GenWave.Host", orchestrationReferences);

            // The load-bearing check: the REAL production DI graph (AddGenWavePlayout, booted via
            // PlayoutClosureWebFactory with Community:CatalogIndexUrl pointed at an unreachable
            // origin), walked outward from the two real tick-path types — PlayoutFeederService and
            // PlayoutSupervisor — through every constructor parameter, resolved via the SAME live
            // container, recursively (the shared walk: <see cref="PlayoutDependencyClosure"/>, moved
            // out of this file at review round 2 finding F6 — it and Story324_RespellOracle's own
            // fact were byte-identical copies of the same walk). No GenWave.Host.Catalog.* type or
            // CommunityCatalogAccessor may appear anywhere in that closure. This is what actually
            // catches a catalog fetch spliced into PlayoutFeederService.ExecuteAsync's tick loop
            // (whether via a new typed constructor parameter or a resolved dependency further down
            // the graph) — a same-assembly wiring change the reference-direction check above cannot
            // see.
            await using var factory = new PlayoutClosureWebFactory(
                catalogIndexUrl: "https://catalog.test/unreachable/index.json");
            var services = factory.Services;

            var closure = PlayoutDependencyClosure.Collect(services);

            var offenders = closure
                .Where(type => type == typeof(CommunityCatalogAccessor)
                    || (type.Namespace ?? "").StartsWith("GenWave.Host.Catalog", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(offenders);

            // Sanity: prove the walk actually reached real playout dependencies, not an empty/broken
            // graph that would trivially pass the assertion above for the wrong reason.
            Assert.Contains(closure, type => type == typeof(PlayoutFeeder));
            Assert.Contains(closure, type => type == typeof(Orchestrator));
        }
    }

    public sealed class ScenarioPolicyParityWithTheImportEndpoint
    {
        // Given unauthenticated and under-privileged callers.

        const string CatalogIndexUrl = "https://catalog.test/parity/index.json";

        [Fact]
        public async Task UnauthenticatedCatalogCallMatchesImportEndpointResponse()
        {
            await using var factory = new ShelfCannotTouchAirWebFactory(CatalogIndexUrl);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var catalogResponse = await client.GetAsync("/api/catalog/index");
            var importResponse = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/personas/valid-dj/import"));

            // Both endpoints carry [AdminSurface] + [Authorize(Policy = Settings)] — an unauthenticated
            // caller is rejected by the SAME cookie-auth events (AdminApiServiceCollectionExtensions'
            // OnRedirectToLogin) before either controller is ever instantiated, so the two responses
            // must be byte-identical.
            Assert.Equal(HttpStatusCode.Unauthorized, catalogResponse.StatusCode);
            Assert.Equal(catalogResponse.StatusCode, importResponse.StatusCode);
            Assert.Equal(
                await catalogResponse.Content.ReadAsStringAsync(),
                await importResponse.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task UnderPrivilegedCatalogAccessMatchesImportEndpointPolicy()
        {
            // This codebase's admin plane is single-tier today (gh-#8): AdminOnlyAuthorizationHandler
            // succeeds for ANY authenticated cookie session regardless of which named policy
            // (AdminOnly/Operator/Curation/Settings/PlayoutRead) an endpoint declares — pinned live by
            // Gh008_GranularAdminPolicies.cs's own ALoggedInAdminIsNeverForbiddenByThePlaneSplit fact —
            // and the single shared admin password is the only credential this app can ever issue.
            // There is therefore no live HTTP request today that produces an authenticated-but-denied-
            // for-Settings caller to send (no call is made here at all): the RBAC seam does NOT
            // support constructing one over HTTP (the escape hatch this fact's own PLAN task line
            // calls out).
            //
            // The honest, non-tautological parity the seam DOES support — and gh-#8's own docs promise
            // ("every controller already declares which plane it belongs to... a future RBAC module
            // differentiates... never by touching controllers") — is structural: CatalogController and
            // PersonaController.Import must be wired to the IDENTICAL named policy today, or an
            // "authenticated with a policy that isn't Settings" caller, the moment RBAC differentiates
            // for real, gets silently different treatment at the two surfaces. Pinned here off the REAL
            // route table (not a hand-copied assumption) — this is what would catch someone quietly
            // loosening CatalogController from Settings to a lesser plane (Operator/Curation/
            // PlayoutRead) without touching PersonaController — plus a live IAuthorizationService check
            // (mirrors Story163_NamedAuthorizationPolicies.cs's own direct-authorization-service idiom)
            // that the SAME caller gets the SAME, EXPLICIT expected outcome against both real,
            // DI-resolved policies: today that caller (authenticated, no claims) is admitted by BOTH —
            // AdminOnlyRequirement asks only "is this caller authenticated at all" — so the expected
            // result is Succeeded == true at both surfaces, not merely "whatever it is, it matches".
            await using var factory = new ShelfCannotTouchAirWebFactory(CatalogIndexUrl);
            _ = factory.CreateClient(); // force host build

            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
            var catalogPolicy = SinglePolicyName(endpoints, "api/catalog/index");
            var importPolicy = SinglePolicyName(endpoints, "api/personas/{slug}/import");

            Assert.Equal(importPolicy, catalogPolicy);

            var authService = factory.Services.GetRequiredService<IAuthorizationService>();
            var underPrivilegedCaller = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Cookie"));

            var catalogResult = await authService.AuthorizeAsync(underPrivilegedCaller, resource: null, catalogPolicy);
            var importResult = await authService.AuthorizeAsync(underPrivilegedCaller, resource: null, importPolicy);

            Assert.True(catalogResult.Succeeded);
            Assert.True(importResult.Succeeded);
        }

        static string SinglePolicyName(IReadOnlyList<Endpoint> endpoints, string routeRawText)
        {
            var policy = endpoints
                .Single(e => (e as RouteEndpoint)?.RoutePattern.RawText
                    ?.Equals(routeRawText, StringComparison.OrdinalIgnoreCase) == true)
                .Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .SingleOrDefault(p => !string.IsNullOrEmpty(p));

            return policy ?? throw new InvalidOperationException($"No named policy found on route '{routeRawText}'.");
        }
    }
}
