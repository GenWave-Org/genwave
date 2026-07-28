// STORY-251 — Operator override of the explicit classification (SPEC F95.3, F95.5, PLAN T115)
//
// BDD specification — xUnit. Entry-point discipline: every core-body fact below drives the
// deployed production surface (WebApplicationFactory<Program> against the real
// PUT /api/media/{id}/explicit route, real cookie auth via POST /api/auth/login — mirrors
// Story234_CatalogProxyGuardedDoor.cs's CatalogApiWebFactory/LoggedInClientAsync idiom) with
// IMediaExplicitOverride faked at the DI boundary (mirrors Story237's PersonaProvenanceWebFactory
// swapping IPersonaStore) — real JSON in, real JSON out, asserted straight off the response's
// JsonElement rather than reflection on an anonymous type, so a naming/shape regression on the
// wire is caught here and not just at the controller-internals level.
//
// This file is what closed the original fail-open regression (gh review finding #1): a prior
// revision bound the request body to a `bool? Explicit`-typed DTO and constructed
// ExplicitOverrideController directly with an already-parsed C# value — that unit-tests-green path
// never exercised ASP.NET Core's actual JSON model binding, so the DTO's "absent means null means
// clear" behavior shipped undetected. Driving real JSON through the real pipeline is the point.
//
// The real-Postgres behavior behind the seam (the operator stamp, the atomic clear, the
// never-play orthogonality pin) is Story251_ExplicitClassification.cs's job (GenWave.MediaLibrary.Tests)
// — this file cannot reach that seam itself (no ProjectReference to GenWave.MediaLibrary.Tests, no
// Postgres fixture in this project — the same split Story250_AudiencePostureSetting.cs documents
// for its own MediaLibrary/Host halves).

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

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable, call-recording <see cref="IMediaExplicitOverride"/> double. Returns the configured
/// outcome and records every call's arguments so a scenario can assert what
/// <see cref="GenWave.Host.Api.ExplicitOverrideController"/> passed through — registered into the
/// real DI graph in place of the Postgres-backed implementation (see
/// <see cref="ExplicitOverrideApiWebFactory"/>), never constructed against the controller directly.
/// </summary>
file sealed class FakeMediaExplicitOverride : IMediaExplicitOverride
{
    public ExplicitOverrideOutcome Result { get; set; } = new(ExplicitOverrideResult.Updated, true, "operator");

    public List<(long MediaId, bool? ExplicitValue)> Calls { get; } = [];

    public Task<ExplicitOverrideOutcome> SetExplicitOverrideAsync(long mediaId, bool? explicitValue, CancellationToken ct)
    {
        Calls.Add((mediaId, explicitValue));
        return Task.FromResult(Result);
    }
}

// ── WebApplicationFactory driving the real HTTP pipeline ─────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph (routing, cookie auth, the production
/// <c>PUT /api/media/{id}/explicit</c> route) with <see cref="IMediaExplicitOverride"/> replaced by
/// <paramref name="explicitOverride"/> — mirrors Story237's <c>PersonaProvenanceWebFactory</c>
/// swapping <c>IPersonaStore</c>/<c>IPersonaImportStore</c>. <paramref name="withAdminPassword"/>
/// mirrors Story112's <c>RatingApiWebFactory</c>: the 401 case needs the deny-by-default policy
/// active; the 415 case does not — <c>[Consumes]</c> rejection happens during routing/action
/// selection, before auth runs, so it is tested with the API left open.
/// </summary>
file sealed class ExplicitOverrideApiWebFactory(FakeMediaExplicitOverride explicitOverride, bool withAdminPassword = true)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-explicit-override";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope so ValidateOnStart()
        // is satisfied without injecting them manually (mirrors Story112/Story234).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", Password);
        }

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaExplicitOverride>();
            services.AddSingleton<IMediaExplicitOverride>(explicitOverride);
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story234's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureExplicitOverrideEndpoint
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — set/clear round-trip, real JSON in and out
    // ---------------------------------------------------------------------

    public sealed class ScenarioSettingTheOverride
    {
        [Fact]
        public async Task OverrideEndpointStampsSourceOperator()
        {
            // PUT /api/media/42/explicit {"explicit":true} → 200 {"explicit":true,"explicitSource":"operator"} (F95.3).
            var fake = new FakeMediaExplicitOverride
            {
                Result = new ExplicitOverrideOutcome(ExplicitOverrideResult.Updated, true, "operator"),
            };
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/42/explicit", new { @explicit = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(BoolOrNull(body, "explicit"));
            Assert.Equal("operator", StringOrNull(body, "explicitSource"));
            Assert.Equal((42L, (bool?)true), Assert.Single(fake.Calls));
        }

        [Fact]
        public async Task SettingFalseAlsoStampsSourceOperator()
        {
            // {"explicit":false} → 200 {"explicit":false,"explicitSource":"operator"}.
            var fake = new FakeMediaExplicitOverride
            {
                Result = new ExplicitOverrideOutcome(ExplicitOverrideResult.Updated, false, "operator"),
            };
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/42/explicit", new { @explicit = false });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(BoolOrNull(body, "explicit"));
            Assert.Equal("operator", StringOrNull(body, "explicitSource"));
            Assert.Equal((42L, (bool?)false), Assert.Single(fake.Calls));
        }
    }

    public sealed class ScenarioClearingTheOverride
    {
        [Fact]
        public async Task ExplicitNullClearsAndEchoesTheWipedColumns()
        {
            // {"explicit":null} → 200 {"explicit":null,"explicitSource":null} (F95.3) — the ONE
            // legitimate way to clear: an explicit JSON null, never an absent property (see
            // ScenarioRejectingAmbiguousBodies below).
            var fake = new FakeMediaExplicitOverride
            {
                Result = new ExplicitOverrideOutcome(ExplicitOverrideResult.Updated, null, null),
            };
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/42/explicit", new { @explicit = (bool?)null });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Null(BoolOrNull(body, "explicit"));
            Assert.Null(StringOrNull(body, "explicitSource"));
            Assert.Equal((42L, (bool?)null), Assert.Single(fake.Calls));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the fail-open regression this task closes: absence vs. null (F95.3 wire contract)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingAmbiguousBodies
    {
        [Fact]
        public async Task AnEmptyBodyReturns400AndWritesNothing()
        {
            // {} (the "explicit" property absent entirely) → 400. This is the regression this
            // task exists to close: absence must NEVER be treated as a synonym for JSON null —
            // a silent clear would wipe the operator verdict AND the LLM-sweep miss stamp,
            // re-admitting the row on an everyone station until a sweep re-stamps it.
            var fake = new FakeMediaExplicitOverride();
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/42/explicit", new { });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(fake.Calls);
        }

        [Fact]
        public async Task ANonBooleanExplicitValueReturns400AndWritesNothing()
        {
            // {"explicit":"banana"} → 400; a JSON string (or number/array/object) is never a valid
            // tri-state value, and must not fall through to either true/false or a clear.
            var fake = new FakeMediaExplicitOverride();
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/42/explicit", new { @explicit = "banana" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(fake.Calls);
        }
    }

    public sealed class ScenarioRejectingUnknownRows
    {
        [Fact]
        public async Task SetOnAnUnknownIdReturns404()
        {
            // PUT /api/media/999999/explicit → 404 (IDOR-safe: existence-first, no data leaked).
            var fake = new FakeMediaExplicitOverride
            {
                Result = new ExplicitOverrideOutcome(ExplicitOverrideResult.NotFound, null, null),
            };
            await using var factory = new ExplicitOverrideApiWebFactory(fake);
            var client = await ExplicitOverrideApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/media/999999/explicit", new { @explicit = true });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioDenyByDefaultPosture
    {
        [Fact]
        public async Task AWriteWithoutACookieReturns401()
        {
            // Admin:Password set, no cookie → 401 (AdminOnly-plane parity, mirrors Story112).
            var fake = new FakeMediaExplicitOverride();
            await using var factory = new ExplicitOverrideApiWebFactory(fake, withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            var response = await client.PutAsync(
                "/api/media/1/explicit",
                JsonContent.Create(new { @explicit = true }));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AWriteWithoutJsonContentTypeReturns415()
        {
            // Valid cookie, form content-type → 415; nothing written (CSRF posture, mirrors Story112).
            // No Admin:Password set — the factory opens the API so content-type negotiation is
            // tested in isolation, without needing a valid cookie.
            var fake = new FakeMediaExplicitOverride();
            await using var factory = new ExplicitOverrideApiWebFactory(fake, withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "explicit=true",
                System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded");
            var response = await client.PutAsync("/api/media/1/explicit", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reads a nullable bool property off the response's real, serialized JSON.</summary>
    static bool? BoolOrNull(JsonElement root, string name) => root.GetProperty(name).ValueKind switch
    {
        JsonValueKind.True  => true,
        JsonValueKind.False => false,
        JsonValueKind.Null  => null,
        var kind            => throw new InvalidOperationException($"Property '{name}' had unexpected kind {kind}."),
    };

    /// <summary>Reads a nullable string property off the response's real, serialized JSON.</summary>
    static string? StringOrNull(JsonElement root, string name)
    {
        var prop = root.GetProperty(name);
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
    }
}
