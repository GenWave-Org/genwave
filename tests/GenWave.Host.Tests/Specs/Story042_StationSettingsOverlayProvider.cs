// STORY-042 — Station settings overlay store (the IConfigurationProvider / live-reload half)
//
// BDD specification — xUnit. Verifies the custom configuration provider over station.settings
// overlays the env/appsettings defaults and live-reloads via IOptionsMonitor.
//
// Non-Integration specs: pure in-process, no DB. A fake/in-memory settings source drives the provider.
// Integration spec (live reload): in-process using the real provider + a fake connection string that
// initially fails — the reload path is exercised by directly calling provider.Reload() after
// injecting test data, so the change-token path is genuinely exercised without a live DB.
//
// The secrets-never-in-store spec proves the allowlist excludes known secret keys.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationSettingsOverlayProvider
{
    // ── Testable provider subclass ─────────────────────────────────────────────
    // Rather than hitting a DB, we override Load() to seed the data bag directly.
    // This lets us test the IConfigurationProvider contract (TryGet, GetReloadToken, child-key
    // enumeration) plus the change-token wiring without a running Postgres.

    sealed class SeededProvider : StationSettingsConfigurationProvider
    {
        readonly IReadOnlyDictionary<string, string?> seed;

        public SeededProvider(IReadOnlyDictionary<string, string?> seed)
            : base("Host=fakepg;")   // connection string never used; Load is overridden
        {
            this.seed = seed;
        }

        public override void Load()
        {
            foreach (var (key, value) in seed)
                Set(key, value);
        }
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> backed by a base layer plus the seeded overlay,
    /// then wraps it in an <see cref="IOptions{T}"/>-compatible monitor.
    /// </summary>
    static (IConfigurationRoot root, SeededProvider provider) BuildConfig(
        IReadOnlyDictionary<string, string?> baseValues,
        IReadOnlyDictionary<string, string?> overlayValues)
    {
        var provider = new SeededProvider(overlayValues);

        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(baseValues)
            .Add(new ProviderWrapperSource(provider))
            .Build();

        return (root, provider);
    }

    /// <summary>Thin IConfigurationSource that just returns an already-constructed provider.</summary>
    sealed class ProviderWrapperSource(IConfigurationProvider inner) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => inner;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static IOptionsMonitor<LoudnessOptions> BuildMonitor(IConfigurationRoot root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(root);
        services
            .AddOptions<LoudnessOptions>()
            .Bind(root.GetSection(LoudnessOptions.Section));
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<LoudnessOptions>>();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOverlayPrecedence
    {
        [Fact]
        public void AStoredValueOverridesTheAppsettingsDefault()
        {
            // Base layer has TargetLufs = -16; overlay stores -14.
            var (root, _) = BuildConfig(
                new Dictionary<string, string?> { ["Loudness:TargetLufs"] = "-16" },
                new Dictionary<string, string?> { ["Loudness:TargetLufs"] = "-14" });

            var value = root["Loudness:TargetLufs"];
            Assert.Equal("-14", value);
        }

        [Fact]
        public void AnEmptyStoreFallsThroughToTheDefault()
        {
            // Overlay is empty; base layer provides the default.
            var (root, _) = BuildConfig(
                new Dictionary<string, string?> { ["Loudness:TargetLufs"] = "-16" },
                new Dictionary<string, string?>());

            var value = root["Loudness:TargetLufs"];
            Assert.Equal("-16", value);
        }
    }

    public sealed class ScenarioLiveReload
    {
        [Fact, Trait("Category", "Integration")]
        public async Task OptionsMonitorReturnsTheNewValueAfterAWriteWithoutAnApiRestart()
        {
            // Arrange: start with -16 in the overlay.
            var overlay = new Dictionary<string, string?> { ["Loudness:TargetLufs"] = "-16" };
            var (root, provider) = BuildConfig(
                new Dictionary<string, string?> { ["Loudness:TargetLufs"] = "-18" },
                overlay);

            var monitor = BuildMonitor(root);
            Assert.Equal(-16.0, monitor.CurrentValue.TargetLufs);

            var reloaded = new TaskCompletionSource<LoudnessOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = monitor.OnChange(opts => reloaded.TrySetResult(opts));

            // Act: update the in-memory seed and call Reload() — this proves the change-token path.
            overlay["Loudness:TargetLufs"] = "-14";
            provider.Reload();

            // Assert: the monitor fires within a reasonable deadline and the new value is bound.
            var completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(reloaded.Task, completed);
            Assert.Equal(-14.0, monitor.CurrentValue.TargetLufs);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSecretsNeverEnterTheStore
    {
        static readonly string[] SecretKeys =
        [
            "Admin:Password",
            "ConnectionStrings:Library",
            "ConnectionStrings:Station",
            "ConnectionStrings:Identity",
            "ICECAST_SOURCE_PASSWORD",
            "POSTGRES_PASSWORD",
            "LIBRARY_DB_PASSWORD",
            "STATION_DB_PASSWORD",
        ];

        [Fact]
        public void SecretsAreNeverOnTheAllowlist()
        {
            // Every known secret key must be absent from the allowlist.
            // This is the compile-time guarantee: the provider only loads keys on the allowlist,
            // and writes via IStationSettingsStore are rejected for non-allowlisted keys.
            foreach (var secretKey in SecretKeys)
            {
                var onList = StationSettingsAllowlist.ByKey.ContainsKey(secretKey);
                Assert.False(onList, $"Secret key '{secretKey}' must never be on the settings allowlist.");
            }
        }

        [Fact]
        public void AllowlistDoesNotContainConnectionStringsPrefix()
        {
            // Belt-and-suspenders: no allowlisted key starts with "ConnectionStrings" or "Admin:Password".
            foreach (var entry in StationSettingsAllowlist.All)
            {
                Assert.False(
                    entry.Key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase),
                    $"ConnectionStrings keys must never appear on the allowlist (found: {entry.Key})");
                Assert.False(
                    entry.Key.Equals("Admin:Password", StringComparison.OrdinalIgnoreCase),
                    "Admin:Password must never appear on the allowlist");
            }
        }

        [Fact]
        public async Task WriteAsyncRejectsDisallowedKeys()
        {
            // IStationSettingsStore.WriteAsync must throw for keys not on the allowlist.
            // We test through the StationSettingsStore directly (no DB needed — it throws before connecting).
            var source = new StationSettingsConfigurationSource("Host=fakepg;");
            var store = new StationSettingsStore("Host=fakepg;", source);

            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => store.WriteAsync("Admin:Password", "hunter2"));
            Assert.Equal("key", ex.ParamName);
        }
    }
}
