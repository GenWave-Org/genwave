using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Route-table discovery + AdminSurface/Settings-policy shape helpers shared by every disclosure-audit
/// spec that sweeps the app's OWN <see cref="EndpointDataSource"/> rather than a hand-maintained mirror
/// of it (<c>Story278_ThemeCatalogIsolation.cs</c>, <c>Story283_InstalledFontServing.cs</c>,
/// <c>Story289_WardrobeIsolation.cs</c> — extracted here on the THIRD near-verbatim copy, the
/// extract-on-third-copy precedent this test project already follows, e.g.
/// <see cref="SimulatedPortStartupFilter"/>).
///
/// <para>
/// Two independent, PURE concerns live here — WHICH routes exist under a given prefix (discovery), and
/// WHAT an endpoint's own AdminSurface+Settings shape looks like (inspection) — never an ASSERTION of
/// their own: each caller's own claim over those facts differs (Story278/Story289 require EVERY
/// discovered route to carry the AdminSurface+Settings pairing; Story283 requires it CONDITIONALLY,
/// since one route it discovers — the public <c>fonts/{file}</c> serving route — is deliberately the
/// one exception), so bundling an assertion here would force a false choice between them. Each caller
/// keeps its own known-route-set literal and its own assertion strategy; only the shared FACTS those
/// are built from move here.
/// </para>
/// </summary>
static partial class GuardedRouteInspector
{
    /// <summary>Every endpoint whose route pattern IS, or sits immediately under, any of
    /// <paramref name="prefixes"/> — segment-bounded (a route at exactly "api/fontsomething" never
    /// falsely matches "api/fonts").</summary>
    public static List<RouteEndpoint> DiscoverEndpoints(IServiceProvider services, params string[] prefixes) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is { } raw
                && prefixes.Any(prefix => MatchesGuardedPrefix(raw.TrimStart('/'), prefix)))
            .ToList();

    static bool MatchesGuardedPrefix(string route, string prefix) =>
        route == prefix || route.StartsWith(prefix + "/", StringComparison.Ordinal);

    /// <summary>Does <paramref name="endpoint"/> carry <see cref="AdminSurfaceAttribute"/>, and does
    /// its own authorization metadata resolve to EXACTLY ONE non-empty policy,
    /// <see cref="AuthorizationPolicies.Settings"/> — the same explicit shape check every existing
    /// site already inlined (never LINQ's <c>SingleOrDefault</c>, which throws an
    /// <see cref="InvalidOperationException"/> carrying no route/policy context the moment an endpoint
    /// carries two distinct non-empty policies). Returns the raw policy set alongside both booleans so
    /// a caller building its own violation message can name what it actually found.</summary>
    public static (bool CarriesAdminSurface, bool HasExactlySettingsPolicy, string?[] Policies) AdminSurfaceShape(RouteEndpoint endpoint)
    {
        var carriesAdminSurface = endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>() is not null;
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorizeData => authorizeData.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy))
            .Distinct()
            .ToArray();
        var hasExactlySettings = policies is [var onlyPolicy] && onlyPolicy == AuthorizationPolicies.Settings;
        return (carriesAdminSurface, hasExactlySettings, policies);
    }

    /// <summary>Every (verb, route) pair an endpoint is actually mapped on — the shared projection
    /// every route-SET exact-match pin (Story278/Story283's own) reads off, rather than each
    /// re-deriving its own copy of "endpoint × HttpMethodMetadata.HttpMethods".</summary>
    public static IEnumerable<(string Verb, string Route)> RouteVerbPairs(IEnumerable<RouteEndpoint> endpoints) =>
        endpoints.SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            .Select(verb => (Verb: verb, Route: endpoint.RoutePattern.RawText!.TrimStart('/'))));

    /// <summary>Every <c>{param}</c> route-pattern segment (slug, file, …) becomes a literal
    /// placeholder that still matches the pattern's own shape at the ROUTING layer —
    /// <c>SurfaceGateMiddleware</c> decides existence off the MATCHED ENDPOINT's metadata alone, never
    /// off whether the placeholder happens to be a real slug, so any placeholder that routes at all
    /// proves the same thing a real one would. Used by any spec that sends a LIVE request at a
    /// discovered route pattern rather than merely inspecting its metadata (<c>Story289_WardrobeIsolation.cs</c>'s
    /// own positive-control probes).</summary>
    public static string ConcreteRequestPath(string routePatternRawText) =>
        "/" + ParameterSegment().Replace(routePatternRawText.TrimStart('/'), "probe");

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex ParameterSegment();
}
