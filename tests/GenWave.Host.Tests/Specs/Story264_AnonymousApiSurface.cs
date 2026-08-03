// STORY-264 — The anonymous /api/* surface is a closed, named set (T161 review follow-up)
//
// BDD specification — xUnit. T161 (GET /api/theme.css) made this the THIRD anonymous /api/*
// route, joining POST /api/auth/login and POST /api/auth/logout (STORY-163/164). All three
// share one rationale — each must be reachable BEFORE a session cookie exists (login creates
// it, logout clears it, theme.css paints the pre-auth login page itself; see
// AdminThemeEndpoints' own remarks) — but nothing asserted that the set was deliberate. Every
// individual addition looks justified in isolation; only the enumeration catches a fourth
// joining without anyone re-arguing it.
//
// Story163_NamedAuthorizationPolicies already pins F60.2 (every endpoint carries EITHER a named
// policy OR AllowAnonymous) — a materially weaker property than this one: it would pass
// unchanged if a fourth /api/* route turned up anonymous tomorrow. This spec pins the SET
// itself, discovered from the app's own route table (EndpointDataSource + IAllowAnonymous
// metadata) rather than restated as a literal list a silent fourth addition could outgrow
// without this file ever failing.
//
// Filed under STORY-264 (not its own file per story/gh-numbered convention) because T161 is
// what turned two routes into a three-route PATTERN worth naming — but it is a sibling to
// Story172_PublicListenerIsolation.cs's own "these and only these" idiom, not a continuation of
// that story: Story172 pins WHICH SURFACE a route belongs to (port-level, via
// AdminSurface/SpectatorSurface tags); this spec pins WHICH ROUTES ARE ANONYMOUS
// (authorization-level, via AllowAnonymous metadata) — an orthogonal axis, so it does not belong
// in that file. It draws routes from STORY-163/164 (auth) as much as STORY-264 (theme.css); this
// is the one spec that reads the anonymous /api/* surface as a whole rather than from any single
// story's own routes.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>Same idiom as Story163's PoliciesWebFactory — the standard host boot used across
/// authorization-shape specs.</summary>
file sealed class AnonymousApiSurfaceWebFactory() : WebApplicationFactory<Program>
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
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureAnonymousApiSurface
{
    // The set this spec exists to pin. Each member is reachable before a session cookie can
    // exist — that is the whole rationale a fourth addition would have to share, not just look
    // superficially plausible next to.
    static readonly IReadOnlySet<string> ExpectedAnonymousApiRoutes = new HashSet<string>(
        StringComparer.Ordinal) { "api/auth/login", "api/auth/logout", "api/theme.css" };

    public sealed class ScenarioTheAnonymousApiSetIsExactlyThreeRoutes
    {
        [Fact]
        public async Task DiscoveredAnonymousApiRoutesMatchTheNamedSetExactly()
        {
            // Arrange: boot the real host and read its own route table — no hand-maintained
            //          mirror of it. Controller routes report their pattern WITHOUT a leading
            //          '/' (e.g. "api/auth/login"); minimal-API routes report it WITH one (e.g.
            //          "/api/theme.css") — TrimStart('/') normalizes both to one shape.
            await using var factory = new AnonymousApiSurfaceWebFactory();
            _ = factory.CreateClient(); // force host build so the route table is populated
            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

            // Act: discover every /api/* route the app itself marks AllowAnonymous. A route
            //      gated by a named policy (even one like Spectator's that always succeeds)
            //      carries IAuthorizeData, not IAllowAnonymous, so it is correctly excluded here
            //      — this spec is about the ANONYMOUS set specifically, not the Spectator plane.
            var discovered = endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is { } raw
                    && raw.TrimStart('/').StartsWith("api/", StringComparison.Ordinal))
                .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                .Select(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/'))
                .ToHashSet(StringComparer.Ordinal);

            // Assert: the discovered set is EXACTLY the named set — not a subset check, which
            //         would let a fourth anonymous /api/* route join silently.
            Assert.True(ExpectedAnonymousApiRoutes.SetEquals(discovered), FailureMessage(discovered));
        }

        static string FailureMessage(IReadOnlySet<string> discovered)
        {
            var added = discovered.Except(ExpectedAnonymousApiRoutes).ToArray();
            var removed = ExpectedAnonymousApiRoutes.Except(discovered).ToArray();
            return "The anonymous /api/* route set no longer matches the named, deliberate set " +
                $"[{string.Join(", ", ExpectedAnonymousApiRoutes)}]. " +
                (added.Length > 0 ? $"Newly anonymous: [{string.Join(", ", added)}]. " : "") +
                (removed.Length > 0 ? $"No longer anonymous: [{string.Join(", ", removed)}]. " : "") +
                "Every member of this set is reachable before a session cookie exists — that is " +
                "an authorization DECISION, not a side effect of routing. If a new route " +
                "genuinely belongs here, add it to ExpectedAnonymousApiRoutes above AND argue " +
                "why in that endpoint's own remarks (see AdminThemeEndpoints for the shape); if " +
                "it does not, its AllowAnonymous is very likely a bug.";
        }
    }
}
