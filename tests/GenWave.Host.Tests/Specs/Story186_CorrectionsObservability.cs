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
// render produces an Information log line and an incremented per-rule counter, readable back via
// GET /api/tts/corrections-stats. F68.7 originally specified debug; amended to Information by
// F97.5/F100.1 (PLAN T142, 2026-08-12) on the "debug never reaches the fleet log store" ground — the
// level itself is pinned below (ScenarioFiredRuleObservability), not merely message content.

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

/// <summary>Captures every Debug+ log entry — level, category, and message together, so a spec can
/// assert on a specific class's output AND its exact level (PLAN T142 review: message content
/// alone cannot pin the F68.7-to-Information amendment) — mirrors Story164_FailClosedWithoutPassword's
/// CapturingWarningLoggerProvider, lowered to Debug so a regression back to debug is still visible
/// here even though it would no longer be picked up by the "Default: Information" floor.</summary>
file sealed class CapturingDebugLoggerProvider : ILoggerProvider
{
    readonly List<(LogLevel Level, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (entries) return entries.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    void Add(LogLevel level, string message) { lock (entries) entries.Add((level, message)); }

    sealed class Logger(CapturingDebugLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(logLevel, $"[{category}] {formatter(state, exception)}");
        }
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>Boots the real host with the two fakes above swapped in and Debug logging force-enabled
/// for <see cref="NormalizingTtsSynthesizer"/>'s category specifically. The correction-fired line
/// itself now logs at Information (SPEC F68.7 as amended by F97.5/F100.1, PLAN T142), which the
/// appsettings-configured "Default: Information" level already lets through on its own — the
/// targeted <c>AddFilter</c> below is kept anyway, deliberately lower than that: it is what lets
/// <c>The_line_is_emitted_at_information_not_debug</c> tell "logged nothing" apart from "regressed
/// back to debug" (a debug line would still land in <see cref="CapturingDebugLoggerProvider.Entries"/>,
/// just at the wrong <see cref="LogLevel"/>, rather than being silently dropped before this provider
/// ever saw it).</summary>
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
        public async Task Fired_correction_logs_at_information_and_increments_per_rule_counter()
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
            Assert.Equal("coming up a deep cut from muh-cloud.", engine.LastText);

            // Then an Information log line naming the rule exists (SPEC F68.7 as amended by
            // F97.5/F100.1, PLAN T142) — the level is pinned here, not just the message content,
            // so a regression back to debug (still captured by CapturingDebugLoggerProvider's own
            // Debug-and-above floor) fails this assertion instead of passing on message text alone ...
            Assert.Contains(
                logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("MacLeod", StringComparison.Ordinal));

            // ... and the per-rule counter is incremented, readable via the admin stats endpoint.
            var statsResponse = await client.GetAsync("/api/tts/corrections-stats");
            Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);

            var stats = await statsResponse.Content.ReadFromJsonAsync<List<CorrectionStat>>();
            Assert.NotNull(stats);
            var macleod = Assert.Single(stats!, s => s.From.Equals("MacLeod", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, macleod.Fired);
        }

        [Fact]
        public async Task Fired_correction_with_a_newline_cannot_forge_the_log_line()
        {
            // CodeQL cs/log-forging (PLAN T142 review): From is operator-authored data, never
            // trusted verbatim into a log line — pins LogSanitize.Strip at the one call site
            // NormalizingTtsSynthesizer.ReportFiredCorrections converged onto (PronunciationRuleHitReporter's
            // own idiom, one seam over). A raw embedded "\n" in From survives SpeechText.PrepareForCorrections
            // untouched (whitespace collapse runs AFTER corrections in the real Normalize pipeline),
            // so this rule genuinely fires on text carrying the identical newline, not merely on a
            // hand-built match — the production shape, same as the happy-path fact above.
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
                    value = "[{\"from\":\"Mac\\nLeod\",\"to\":\"Muh-cloud\"}]",
                },
            });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview",
                new { text = "Coming up, a deep cut from Mac\nLeod.", voice = "af_heart" });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

            // Then the Information line this rule's OWN firing produced exists AND carries no raw
            // newline. Scoped to the "TTS correction fired" message specifically (not just any
            // Information-level entry) — this provider captures every category the ASP.NET Core
            // pipeline logs at Debug+, not only NormalizingTtsSynthesizer's, so an unscoped
            // "some Information entry has no newline" check would pass vacuously off an unrelated
            // framework log line even with LogSanitize.Strip removed entirely (caught empirically
            // in review). A bare Assert.DoesNotContain would ALSO pass vacuously if this rule never
            // fired at all, so the "this message exists" and "it has no newline" checks are pinned
            // together in one assertion.
            Assert.Contains(
                logs.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("TTS correction fired", StringComparison.Ordinal)
                    && !e.Message.Contains('\n'));
        }
    }
}
