// STORY-186 — Corrections editor in admin (observability slice)
//
// BDD specification — xUnit (SPEC F68.7). Drives the exact production pipeline Story185 exercises
// (WebApplicationFactory<Program>: real routing/auth, real SettingsController + SettingValidator +
// StationSettingsAllowlist, real IOptionsMonitor<TtsCorrectionsOptions> + SpeechCorrectionProvider +
// NormalizingTtsSynthesizer + CorrectionsFiredStats, all from TtsServiceCollectionExtensions.
// AddGenWaveTts) with only the two external-service edges this non-Integration suite cannot reach
// faked out: the Postgres-backed IStationSettingsStore and the outbound Kokoro HTTP call inside
// ITtsSynthesizer — mirrors Story185_CorrectionsLiveWiring's own factory shape (its test doubles are
// file-scoped there, so equivalent ones are redefined here rather than shared).
//
// AC1 (CRUD round-trip) and AC2 (preview parity) are browser-verified in T30's wire acceptance — UI
// territory, not unit-specced here. This spec covers AC3 only: a correction that fires during a real
// render produces a debug log line and an incremented per-rule counter, readable back via
// GET /api/tts/corrections-stats.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Tts;
using Xunit;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>A live-reloadable <see cref="IConfigurationProvider"/> — see
/// Story185_CorrectionsLiveWiring's <c>LiveTestConfigurationProvider</c> for the full rationale;
/// redefined here (file-scoped there too) rather than shared across spec files.</summary>
file sealed class ObservabilityConfigurationProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class ObservabilityConfigurationSource(ObservabilityConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary><see cref="IStationSettingsStore"/> test double standing in for the one thing this
/// non-Integration suite cannot reach — a live Postgres <c>station.settings</c> table. Also drives a
/// REAL live reload of the app's own <see cref="IConfiguration"/>, exactly like Story185's
/// <c>LiveTestSettingsStore</c>.</summary>
file sealed class ObservabilitySettingsStore : IStationSettingsStore
{
    readonly ObservabilityConfigurationProvider provider = new();

    public ObservabilitySettingsStore(IConfiguration configuration)
    {
        ((IConfigurationBuilder)configuration).Add(new ObservabilityConfigurationSource(provider));
    }

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

/// <summary>Stands in for the real Kokoro HTTP call at the innermost <see cref="ITtsSynthesizer"/>
/// seam — wrapped by the REAL <see cref="NormalizingTtsSynthesizer"/>, so the production render path
/// (including its fired-rule observability) is genuinely exercised.</summary>
file sealed class RecordingEngineSynthesizer : ITtsSynthesizer
{
    public string? LastText { get; private set; }

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        LastText = text;
        return Task.FromResult(Path.GetTempFileName());
    }
}

/// <summary>Captures every Debug+ log entry, tagged with its category, so a spec can assert on a
/// specific class's debug output (mirrors Story164_FailClosedWithoutPassword's
/// CapturingWarningLoggerProvider, lowered to Debug).</summary>
file sealed class CapturingDebugLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingDebugLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add($"[{category}] {formatter(state, exception)}");
        }
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>Boots the real host with the two fakes above swapped in and Debug logging force-enabled
/// for <see cref="NormalizingTtsSynthesizer"/>'s category specifically — the appsettings-configured
/// "Default: Information" level would otherwise silently drop its debug line, so a targeted
/// <c>AddFilter</c> (a more specific rule than the null-category "Default" one) is added rather than
/// relying on a blanket <c>SetMinimumLevel</c> that config would still out-rank.</summary>
file sealed class CorrectionsObservabilityWebFactory(
    RecordingEngineSynthesizer engine, CapturingDebugLoggerProvider logs) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x8a2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // AddMediaLibrary/AddGenWaveAdminApi read these at composition time in Program.cs —
        // UseSetting (colon-form) reaches those reads (verified empirically), so no process env
        // var is mutated and no other test class can race with this per-instance value.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("GenWave.Tts.NormalizingTtsSynthesizer", LogLevel.Debug);
            logging.AddProvider(logs);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new ObservabilitySettingsStore(sp.GetRequiredService<IConfiguration>()));

            services.RemoveAll<ITtsSynthesizer>();
            services.AddSingleton<ITtsSynthesizer>(sp =>
                new NormalizingTtsSynthesizer(
                    engine,
                    sp.GetRequiredService<SpeechCorrectionProvider>(),
                    sp.GetRequiredService<ActivePersonaCorrectionsCache>(),
                    sp.GetRequiredService<CorrectionsFiredStats>(),
                    sp.GetRequiredService<ILogger<NormalizingTtsSynthesizer>>()));
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Wire shape of one row from <c>GET /api/tts/corrections-stats</c> — mirrors
/// GenWave.Host.Api.CorrectionStatDto without depending on it directly.</summary>
file sealed record CorrectionStat(string From, long Fired);

public static class FeatureCorrectionsObservability
{
    public sealed class ScenarioFiredRuleObservability
    {
        [Fact]
        public async Task Fired_correction_logs_debug_and_increments_per_rule_counter()
        {
            // Given a correction saved via PUT /api/settings ...
            var engine = new RecordingEngineSynthesizer();
            var logs = new CapturingDebugLoggerProvider();
            await using var factory = new CorrectionsObservabilityWebFactory(engine, logs);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { password = CorrectionsObservabilityWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var put = await client.PutAsJsonAsync("/api/settings", new[]
            {
                new
                {
                    key = "Tts:Corrections",
                    value = "[{\"from\":\"MacLeod\",\"to\":\"Muh-cloud\"}]",
                },
            });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            // When a real render fires it — POST /api/tts/preview is the same production hand-off
            // (NormalizingTtsSynthesizer) every render path shares (F68.1) ...
            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview",
                new { text = "Coming up, a deep cut from MacLeod.", voice = "af_heart" });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal("Coming up, a deep cut from Muh-cloud.", engine.LastText);

            // Then a debug log line naming the rule exists (F68.7) ...
            Assert.Contains(logs.Messages, m => m.Contains("MacLeod", StringComparison.Ordinal));

            // ... and the per-rule counter is incremented, readable via the admin stats endpoint.
            var statsResponse = await client.GetAsync("/api/tts/corrections-stats");
            Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);

            var stats = await statsResponse.Content.ReadFromJsonAsync<List<CorrectionStat>>();
            Assert.NotNull(stats);
            var macleod = Assert.Single(stats!, s => s.From.Equals("MacLeod", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, macleod.Fired);
        }
    }
}
