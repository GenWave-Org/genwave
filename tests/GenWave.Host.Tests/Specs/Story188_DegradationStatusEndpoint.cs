// STORY-188 — LLM degradation modes with operator pin (WIRE, admin-status half)
//
// BDD specification — xUnit (SPEC F69.3, F69.5). Implements PLAN T32's own acceptance criterion:
// "mode transition observable via real admin status request while a playout cycle completes in
// every mode." Boots the REAL production DI graph (Program.cs's own AddGenWaveTts wiring —
// DegradationController, DegradationGatedCopyWriter, StatusController, SettingsController,
// SettingValidator, StationSettingsAllowlist entry — none of it hand-built for this spec) with only
// the external-service edges this non-Integration suite cannot reach faked out (Postgres-backed
// IStationSettingsStore/IMediaCatalog/IActivePersonaAccessor, the Kokoro HTTP call inside
// ITtsSynthesizer, and the ffmpeg-backed ILoudnessAnalyzer/ICueAnalyzer) — mirrors Story185's
// CorrectionsLiveWiringWebFactory shape. Llm:Endpoint stays unset throughout (Development's own
// default), so every pinned mode renders through the identical template rung — the per-mode
// copy-writer routing itself (Soft/Hard bypass the LLM even when it WOULD succeed) is proven
// against a real MockCompletionsServer in GenWave.Tts.Tests/Specs/Story188_LlmDegradationModes.cs;
// this file's job is the wire: a live PUT to Llm:DegradationPin reaches GET /api/status, and
// playout (ITtsSegmentSource, the real singleton the feeder itself resolves) still completes.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A live-reloadable <see cref="IConfigurationProvider"/> — same shape as Story185's
/// <c>LiveTestConfigurationProvider</c> (that one is <c>file</c>-scoped to its own spec file, so
/// this is a deliberate small duplicate, not a reuse).
/// </summary>
file sealed class LiveTestConfigurationProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class LiveTestConfigurationSource(LiveTestConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary>
/// <see cref="IStationSettingsStore"/> double that also drives a REAL live reload of the app's own
/// <see cref="IConfiguration"/> (mirrors Story185's <c>LiveTestSettingsStore</c>) — a
/// <c>PUT /api/settings</c> in these specs makes <c>IOptionsMonitor&lt;LlmOptions&gt;</c> genuinely
/// re-bind, the exact mechanism <see cref="DegradationController.Evaluate"/> depends on to see a
/// pin change on its very next call.
/// </summary>
file sealed class LiveTestSettingsStore : IStationSettingsStore
{
    readonly LiveTestConfigurationProvider provider = new();

    public LiveTestSettingsStore(IConfiguration configuration) =>
        ((IConfigurationBuilder)configuration).Add(new LiveTestConfigurationSource(provider));

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        provider.SetAndReload(key, value?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Stands in for the real Kokoro HTTP call — returns an on-disk (empty) file immediately.</summary>
file sealed class RecordingEngineSynthesizer : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        Task.FromResult(Path.GetTempFileName());
}

/// <summary>
/// Boots the real host with a valid admin password (cookie auth) and every external-service edge
/// this non-Integration suite cannot reach faked out — everything else (routing, auth,
/// StatusController, SettingsController, SettingValidator, the allowlist, DegradationController,
/// DegradationGatedCopyWriter, TtsSegmentSource) is the genuine production wiring. Mirrors Story185's
/// CorrectionsLiveWiringWebFactory shape.
/// </summary>
file sealed class DegradationStatusWebFactory : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    const string AdminPasswordVar = "Admin__Password";
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually. Llm:Endpoint stays unset
        // (empty) — this suite never needs a real/mock LLM endpoint (see the file header).
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap/DB connections, and no background
            // Orchestrator/feeder tick that could otherwise render a segment on its own during
            // this test.
            services.RemoveAll<IHostedService>();

            // The one Postgres-backed edge this suite cannot reach — still drives a REAL live
            // reload (see LiveTestSettingsStore's own remarks).
            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new LiveTestSettingsStore(sp.GetRequiredService<IConfiguration>()));

            // IMediaCatalog/IActivePersonaAccessor need a live Postgres in production — faked so
            // this suite never touches one (mirrors Story125's LlmStatusWebFactory).
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // The real Kokoro HTTP call and the real ffmpeg-backed analyzers are the remaining
            // edges a "playout cycle completes" proof would otherwise need a live media stack for —
            // faked so RenderAsync completes deterministically in-process.
            services.RemoveAll<ITtsSynthesizer>();
            services.AddSingleton<ITtsSynthesizer>(new RecordingEngineSynthesizer());
            services.RemoveAll<ILoudnessAnalyzer>();
            services.AddSingleton<ILoudnessAnalyzer>(new FakeLoudnessAnalyzer());
            services.RemoveAll<ICueAnalyzer>();
            services.AddSingleton<ICueAnalyzer>(new FakeCueAnalyzer());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // AddMediaLibrary/AddGenWaveAdminApi read these before any ConfigureWebHost hook is visible
        // to Program.cs (see EnvVarMutatingWebFactoryCollection's remarks) — injected via the
        // environment, same as Story125's/Story185's factories.
        var prevLibrary = Environment.GetEnvironmentVariable(LibraryConnVar);
        var prevAdmin = Environment.GetEnvironmentVariable(AdminPasswordVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable(AdminPasswordVar, Password);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prevLibrary);
            Environment.SetEnvironmentVariable(AdminPasswordVar, prevAdmin);
        }
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureDegradationStatusEndpoint
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioModeTransitionObservableDuringPlayout
    {
        [Fact]
        public async Task Pinning_each_mode_is_visible_on_status_while_playout_completes()
        {
            await using var factory = new DegradationStatusWebFactory();
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { password = DegradationStatusWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var segmentSource = factory.Services.GetRequiredService<ITtsSegmentSource>();

            foreach (var mode in new[] { "normal", "soft", "hard" })
            {
                // Given the pin setting written via a real PUT /api/settings ...
                var put = await client.PutAsJsonAsync(
                    "/api/settings", new[] { new { key = "Llm:DegradationPin", value = mode } });
                Assert.Equal(HttpStatusCode.OK, put.StatusCode);

                // When the admin status endpoint is read (a real GET, over real routing/auth) ...
                var statusResponse = await client.GetAsync("/api/status");
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
                var degradation = status.GetProperty("degradation");

                // Then the transition is observable on that real request ...
                Assert.Equal(mode, degradation.GetProperty("mode").GetString());
                Assert.True(degradation.GetProperty("pinned").GetBoolean());

                // ... while a playout cycle (the real ITtsSegmentSource singleton the feeder
                // itself resolves) still completes in this very mode.
                var rendered = await segmentSource.RenderAsync(LeadInRequest(), CancellationToken.None);
                Assert.NotNull(rendered);
            }
        }
    }
}
