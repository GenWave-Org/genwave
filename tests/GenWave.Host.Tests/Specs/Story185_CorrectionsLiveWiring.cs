// STORY-185 — Corrections live from settings through one call site (WIRE)
//
// BDD specification — xUnit (SPEC F68.1, F68.5, F68.8). Implements PLAN T29's 3 pending facts.
//
// AC1 drives the real production pipeline end to end: WebApplicationFactory<Program> (real
// routing/auth, real SettingsController + SettingValidator + StationSettingsAllowlist entry, real
// IOptionsMonitor<TtsCorrectionsOptions> + SpeechCorrectionProvider + NormalizingTtsSynthesizer —
// all from TtsServiceCollectionExtensions.AddGenWaveTts, none of it hand-built for this spec) with
// only the two external-service edges this non-Integration suite cannot reach faked out: the
// Postgres-backed IStationSettingsStore (mirrors this project's own established DB-substitution
// convention, e.g. Story058's SafeScopeFakeSettingsStore) and the outbound Kokoro HTTP call inside
// ITtsSynthesizer. Unlike a plain call-counting fake, LiveTestSettingsStore below still drives a
// REAL IOptionsMonitor rebind — see its own remarks for how.
//
// AC2 is a source/route audit: exactly one call to SpeechText.Normalize exists anywhere under src/
// (NormalizingTtsSynthesizer), and every production caller of ITtsSynthesizer depends on the
// interface, never the concrete KokoroTtsSynthesizer — so that one registration is the only hand-off
// any of them can reach.
//
// AC3 reads the demo settings seed (compose.demo.yaml) for the MacLeod rule and proves
// TtsCorrectionsOptions itself carries no baked-in default — the seed is data, not code.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A live-reloadable <see cref="IConfigurationProvider"/>. <see cref="ConfigurationProvider"/>'s
/// base class already gives a mutable key/value bag (<c>Set</c>) — this only exposes the protected
/// <c>OnReload</c> so a test can raise the same change-token signal a real settings write raises in
/// production (<c>StationSettingsConfigurationProvider.Reload</c>), which is what
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> actually listens for.
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
/// <see cref="IStationSettingsStore"/> test double standing in for the one thing this
/// non-Integration suite cannot reach — a live Postgres <c>station.settings</c> table (mirrors
/// Story058's <c>SafeScopeFakeSettingsStore</c>). Unlike that double, <see cref="WriteAsync"/> here
/// also drives a REAL live reload of the app's own <see cref="IConfiguration"/>: the constructor
/// appends a <see cref="LiveTestConfigurationProvider"/> to the SAME <see cref="ConfigurationManager"/>
/// the real <c>StationSettingsConfigurationProvider</c> would have appended to (ASP.NET Core
/// registers that manager instance as the app's <see cref="IConfiguration"/> singleton), so a write
/// here makes <c>IOptionsMonitor&lt;TtsCorrectionsOptions&gt;</c> genuinely re-bind — the exact
/// mechanism <c>StationSettingsStore.WriteAsync</c> triggers in production via
/// <c>StationSettingsConfigurationProvider.Reload()</c>, just with Postgres swapped for an
/// in-memory provider.
/// </summary>
file sealed class LiveTestSettingsStore : IStationSettingsStore
{
    readonly LiveTestConfigurationProvider provider = new();

    public LiveTestSettingsStore(IConfiguration configuration)
    {
        ((IConfigurationBuilder)configuration).Add(new LiveTestConfigurationSource(provider));
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

/// <summary>
/// Stands in for the real Kokoro HTTP call at the innermost <see cref="ITtsSynthesizer"/> seam —
/// wrapped by the REAL <see cref="NormalizingTtsSynthesizer"/>, so the production Normalize call
/// site is genuinely exercised and this only records what text reached "the engine".
/// </summary>
file sealed class RecordingEngineSynthesizer : ITtsSynthesizer
{
    public string? LastText { get; private set; }

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        LastText = text;
        return Task.FromResult(Path.GetTempFileName());
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real host with a valid admin password (cookie auth) and the two fakes above swapped
/// in — everything else (routing, auth, <see cref="SettingsController"/>, <see cref="SettingValidator"/>,
/// the allowlist, options binding, <see cref="SpeechCorrectionProvider"/>,
/// <see cref="NormalizingTtsSynthesizer"/>, <see cref="TtsPreviewController"/>) is the genuine
/// production wiring. Mirrors Story084's <c>StatusApiWebFactory</c>/Story058's
/// <c>SettingsApiWebFactory</c> shape.
/// </summary>
file sealed class CorrectionsLiveWiringWebFactory(RecordingEngineSynthesizer engine) : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    const string AdminPasswordVar = "Admin__Password";
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();

            // The one Postgres-backed edge this suite cannot reach — see LiveTestSettingsStore's
            // own remarks for why this still exercises a genuine live-reload round trip.
            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new LiveTestSettingsStore(sp.GetRequiredService<IConfiguration>()));

            // The one network edge this suite cannot reach — the real SpeechCorrectionProvider
            // singleton (AddGenWaveTts) still decorates it via the real NormalizingTtsSynthesizer.
            services.RemoveAll<ITtsSynthesizer>();
            services.AddSingleton<ITtsSynthesizer>(sp =>
                new NormalizingTtsSynthesizer(engine, sp.GetRequiredService<SpeechCorrectionProvider>()));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // AddMediaLibrary/AddGenWaveAdminApi read these before any ConfigureWebHost hook is visible
        // to Program.cs (see EnvVarMutatingWebFactoryCollection's remarks) — injected via the
        // environment, same as Story058's/Story084's factories.
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

public static class FeatureCorrectionsLiveWiring
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (the Story074/
    /// Story102/Story107/Story151/Story160 RepoRoot convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string SrcRoot => Path.Combine(RepoRoot, "src");

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioLiveApply
    {
        [Fact]
        public async Task Saved_correction_applies_to_the_next_render_without_restart()
        {
            // Given the host running and a correction saved via PUT /api/settings ...
            var engine = new RecordingEngineSynthesizer();
            await using var factory = new CorrectionsLiveWiringWebFactory(engine);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { password = CorrectionsLiveWiringWebFactory.Password });
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

            // When the next booth-bound string is rendered — POST /api/tts/preview is the same
            // production hand-off (NormalizingTtsSynthesizer) every render path shares (F68.1) ...
            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview",
                new { text = "Coming up, a deep cut from MacLeod.", voice = "af_heart" });

            // Then the correction reached the engine — no restart, just the PUT above (F68.5).
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal("Coming up, a deep cut from Muh-cloud.", engine.LastText);
        }
    }

    public sealed class ScenarioSingleChokepoint
    {
        [Fact]
        public void Exactly_one_call_site_invokes_normalize()
        {
            var callSites = FindNormalizeCallSites();

            var callSite = Assert.Single(callSites);
            Assert.Equal("NormalizingTtsSynthesizer.cs", Path.GetFileName(callSite));
        }

        [Fact]
        public void EveryProductionCallerDependsOnTheInterfaceNeverTheConcreteSynthesizer()
        {
            // TtsSegmentSource (patter), SafeSegmentAuthor (authored/safe-loop segments), and
            // TtsPreviewController (admin preview) are every shipped caller of "the TTS renderer".
            // None constructs KokoroTtsSynthesizer directly, so the ONE DI registration
            // (NormalizingTtsSynthesizer decorating it — TtsServiceCollectionExtensions) is the
            // only hand-off any of them can reach (F68.1).
            AssertDependsOnInterfaceOnly(typeof(TtsSegmentSource));
            AssertDependsOnInterfaceOnly(typeof(SafeSegmentAuthor));
            AssertDependsOnInterfaceOnly(typeof(TtsPreviewController));
        }

        static void AssertDependsOnInterfaceOnly(Type type)
        {
            var paramTypes = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToList();

            Assert.Contains(typeof(ITtsSynthesizer), paramTypes);
            Assert.DoesNotContain(typeof(KokoroTtsSynthesizer), paramTypes);
        }

        static IReadOnlyList<string> FindNormalizeCallSites()
        {
            const string callSyntax = "SpeechText.Normalize(";
            var hits = new List<string>();

            foreach (var file in Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories))
            {
                // SpeechText.cs is the definition, not a call site.
                if (Path.GetFileName(file) == "SpeechText.cs")
                    continue;

                var text = File.ReadAllText(file);
                var count = 0;
                var index = 0;
                while ((index = text.IndexOf(callSyntax, index, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    index += callSyntax.Length;
                }

                for (var i = 0; i < count; i++)
                    hits.Add(file);
            }

            return hits;
        }
    }

    public sealed class ScenarioDemoSeed
    {
        [Fact]
        public void MacLeodRuleIsSeededInComposeDemoYamlAsSettingsData()
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, "compose.demo.yaml"));

            Assert.Contains("Tts__Corrections:", text, StringComparison.Ordinal);
            Assert.Contains("\"from\":\"MacLeod\"", text, StringComparison.Ordinal);
            Assert.Contains("\"to\":\"Muh-cloud\"", text, StringComparison.Ordinal);
        }

        [Fact]
        public void TtsCorrectionsOptionsCarriesNoBuiltInDefault()
        {
            // The demo seed is the ONLY place the MacLeod rule lives — TtsCorrectionsOptions
            // itself must yield no corrections at all when unconfigured (F68.8).
            var unconfigured = new TtsCorrectionsOptions();

            Assert.Null(unconfigured.Corrections);
        }
    }
}
