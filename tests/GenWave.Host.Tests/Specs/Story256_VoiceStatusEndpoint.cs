// STORY-256/STORY-257 — degraded voice on the health surface (WIRE, api half, PLAN T149)
//
// BDD specification — xUnit (SPEC F99.5, F100.3, STORY-256 AC4). GET /api/status gains the
// `voice` field VoiceHealthReader (GenWave.Tts) builds — this file is the wire pin the T149
// review round 1 found missing: without it, deleting StatusController's `voice` block leaves
// every other gate (including the unit-level VoiceHealthReader specs in
// GenWave.Tts.Tests/Specs/Story256_NeverSomeoneElsesVoice.cs) green, because nothing exercised
// the real production DI graph resolving VoiceHealthReader through the controller onto the wire.
//
// Shares Story188_DegradationStatusEndpoint.cs's own ingredients (WebApplicationFactory, login, a
// real GET, a real DependencyHealthStore seed) — the engine-down half of that same response — but
// not its per-method `await using var factory` shape: this file uses the IAsyncLifetime fixture
// idiom instead, the shape 18 other sibling spec files in this suite already share.
// STORY-256 AC4's own acceptance: "engine down" (this file's `voice`) must be
// distinguishable from "the DJ has nothing to say" (DegradationController's own `degradation`)
// on ONE response — ScenarioEngineDownIsVisibleOnStatus's DegradationModeStaysIndependentOfTheVoiceVerdict
// fact pins exactly that.
//
// See docs/PLAN.md T149.

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
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fake / factory ───────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real host with a valid admin password (cookie auth) — mirrors Story188's
/// <c>DegradationStatusWebFactory</c> / Story084's <c>StatusApiWebFactory</c>. Everything except
/// <see cref="IMediaCatalog"/>/<see cref="IActivePersonaAccessor"/> (the two Postgres-backed edges
/// this non-Integration suite cannot reach) and the hosted services (no Liquidsoap/DB connections,
/// no background tick) is the genuine production wiring — including
/// <see cref="DependencyHealthStore"/>/<see cref="VoiceHealthReader"/>, resolved exactly as
/// <c>TtsServiceCollectionExtensions.AddGenWaveTts</c> registers them. <c>Llm:Endpoint</c> is set
/// to a non-empty (unreachable) value so <see cref="DegradationController.Evaluate"/>'s NOT
/// CONFIGURED branch never fires — with no hosted services running, nothing ever records an LLM
/// call outcome or an Ollama probe verdict, so the auto path's <c>TryDrop</c>/<c>TryRaise</c> both
/// find nothing to act on and <c>degradation.mode</c> settles on Normal by construction. This is a
/// deliberate departure from pinning the mode: a pin short-circuits
/// <see cref="DegradationController.Evaluate"/> before it ever reads a verdict, which would hide a
/// real coupling bug (an engine-down voice verdict wrongly forcing <c>degradation.mode</c>) behind
/// a value that can never move — the AC4 fact needs the auto path actually evaluated so a
/// regression there still reds.
/// </summary>
file sealed class VoiceStatusWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Llm:Endpoint", "http://llm.invalid");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections, and no background
            // Orchestrator/feeder/probe tick that could otherwise overwrite this suite's seeded
            // DependencyHealthStore verdict mid-test.
            services.RemoveAll<IHostedService>();

            // Replace IMediaCatalog with the controllable fake (the real MediaRepository requires
            // a live Postgres and must not be resolved during this test) — mirrors Story084's
            // StatusApiWebFactory.
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));

            // Replace IActivePersonaAccessor for the same reason: the real implementation resolves
            // through Postgres-backed stores before ever answering.
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureVoiceStatusEndpoint
{
    static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = VoiceStatusWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    // ── HAPPY PATH — engine down is visible, and distinguishable from "nothing to say" ─────────────
    // One shared fixture polls once; four facts assert different properties of that one response.

    public sealed class ScenarioEngineDownIsVisibleOnStatus : IAsyncLifetime
    {
        WebApplicationFactory<Program> factory = null!;
        JsonElement voice;
        JsonElement degradation;

        public async Task InitializeAsync()
        {
            factory = new VoiceStatusWebFactory();
            var client = factory.CreateClient();
            await LoginAsync(client);

            // Seed an unhealthy verdict for the primary engine directly in the real store — the
            // exact singleton DependencyHealthProber would otherwise write to on a real probe
            // failure (STORY-187), and the exact singleton VoiceHealthReader reads.
            factory.Services.GetRequiredService<DependencyHealthStore>()
                .Record(DependencyNames.Kokoro, healthy: false, "connection refused");

            var response = await client.GetAsync("/api/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            voice = body.GetProperty("voice");
            degradation = body.GetProperty("degradation");
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public void EngineNamesThePrimaryEngine() =>
            Assert.Equal(DependencyNames.Kokoro, voice.GetProperty("engine").GetString());

        [Fact]
        public void DegradedIsTrue() =>
            Assert.True(voice.GetProperty("degraded").GetBoolean());

        [Fact]
        public void ReasonCarriesTheSeededReason() =>
            Assert.Equal("connection refused", voice.GetProperty("reason").GetString());

        // STORY-256 AC4's own acceptance criterion: "engine down" and "the DJ has nothing to say"
        // must be distinguishable on ONE response. Nothing in this scenario ever records an LLM
        // call outcome or an Ollama probe verdict, so DegradationController.Evaluate's auto path
        // settles on Normal — DegradationController never reads a TTS engine's verdict (only
        // DependencyNames.Ollama's) — so a degraded voice on this very response must never bleed
        // into degradation.mode.
        [Fact]
        public void DegradationModeStaysIndependentOfTheVoiceVerdict() =>
            Assert.Equal("normal", degradation.GetProperty("mode").GetString());
    }

    // ── HAPPY PATH — recovery is visible on the very next poll, no restart ──────────────────────────

    public sealed class ScenarioEngineRecoveryIsVisibleOnStatus : IAsyncLifetime
    {
        WebApplicationFactory<Program> factory = null!;
        JsonElement voice;

        public async Task InitializeAsync()
        {
            factory = new VoiceStatusWebFactory();
            var client = factory.CreateClient();
            await LoginAsync(client);

            var store = factory.Services.GetRequiredService<DependencyHealthStore>();

            // Given the engine started out degraded (the same seed as ScenarioEngineDownIsVisibleOnStatus)...
            store.Record(DependencyNames.Kokoro, healthy: false, "connection refused");
            var degradedResponse = await client.GetAsync("/api/status");
            Assert.Equal(HttpStatusCode.OK, degradedResponse.StatusCode);

            // When the engine recovers and the operator re-polls (no api restart in between,
            // mirroring the P9 stale-snapshot discipline every StatusController field follows)...
            store.Record(DependencyNames.Kokoro, healthy: true, reason: null);
            var recoveredResponse = await client.GetAsync("/api/status");
            Assert.Equal(HttpStatusCode.OK, recoveredResponse.StatusCode);
            var body = await recoveredResponse.Content.ReadFromJsonAsync<JsonElement>();
            voice = body.GetProperty("voice");
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public void DegradedGoesFalseOnRecovery() =>
            Assert.False(voice.GetProperty("degraded").GetBoolean());

        [Fact]
        public void ReasonGoesNullOnRecovery() =>
            Assert.Equal(JsonValueKind.Null, voice.GetProperty("reason").ValueKind);
    }
}
