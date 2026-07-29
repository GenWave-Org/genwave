// gh-#117 — The DJ's clock follows the station, not the container: Station:Timezone
//
// BDD specification — xUnit, modeled end to end on Story250_AudiencePostureSetting.cs (the
// Station:Audience allowlist/validator/live-seam precedent this setting copies). Three halves:
//
//   - ScenarioTheSettingExists: the allowlist entry (Live, String), the honest-blank default
//     (empty = "use the container's own clock" — deliberately NOT seeded in appsettings.json;
//     Story151's HonestlyBlankKeys carries the companion pin), and the SettingValidator guard
//     (empty or a resolvable IANA id; garbage is a 400 naming an example).
//
//   - ScenarioTheClockFollowsTheStation: OptionsMonitorStationClockProvider against the
//     Story042/Story138/Story250 live-reload rig — a real PUT /api/settings repoints the very
//     next LocalNow read with no api restart, and the America/Edmonton conversion is pinned
//     across a DST boundary (MST -07:00 in January, MDT -06:00 in July) so "store the id,
//     convert per read" can never regress into "store an offset once".
//
//   - ScenarioAnUnresolvableZoneNeverFaultsThePatterPath: garbage can only arrive via the
//     environment (the validator 400s it on the settings-API path) — the provider falls back to
//     the container's own clock rather than throwing into every prompt build.
//
// The consumer-side pins live where their subjects compile (the Story117/121 split):
// Tts.Tests/Specs/Story193_PersonaPromptAssemblyAndClock.cs (the prompt's clock line) and
// Orchestration.Tests/Specs/Gh117_StationLocalSegmentClock.cs (SegmentRequest.LocalNow).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationTimezoneSetting
{
    const string Key = "Station:Timezone";

    /// <summary>Repo root, resolved relative to the test assembly's build output — the
    /// Story074/Story102/Story151/Story250 convention for reaching repo-root files from a test
    /// project.</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    public sealed class ScenarioTheSettingExists
    {
        // Given a fresh station (gh-#117).

        [Fact]
        public void KeyIsAllowlistedAndLiveApply()
        {
            Assert.True(StationSettingsAllowlist.ByKey.ContainsKey(Key));

            var allowed = StationSettingsAllowlist.ByKey[Key];
            Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            Assert.Equal(SettingKind.String, allowed.Kind);
        }

        [Fact]
        public void EmptyIsTheHonestDefaultAndNothingSeedsIt()
        {
            // Empty = "use the container's own clock" (pre-gh-#117 behavior, byte-identical) —
            // the Llm:Endpoint/Station:PublicStreamUrl honest-blank shape, NOT the F55.1 seeded
            // shape: seeding a concrete zone would silently repoint every fresh deploy's DJ clock.
            Assert.Equal(string.Empty, new StationOptions().Timezone);

            var config = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();
            Assert.True(string.IsNullOrEmpty(config[Key]));
        }

        [Fact]
        public void EmptyAndResolvableIanaIdsAreAccepted()
        {
            var validator = BuildValidator();

            Assert.Null(validator.Validate(Key, ""));
            Assert.Null(validator.Validate(Key, "America/Edmonton"));
            Assert.Null(validator.Validate(Key, "Europe/Berlin"));
            Assert.Null(validator.Validate(Key, "UTC"));
        }

        [Fact]
        public void GarbageIsRejectedWithAnErrorNamingIanaAndAnExample()
        {
            var validator = BuildValidator();

            Assert.NotNull(validator.Validate(Key, "Not/AZone"));
            Assert.NotNull(validator.Validate(Key, "garbage"));
            Assert.NotNull(validator.Validate(Key, "MST or something"));

            // The 400 an operator actually reads: says what shape is wanted and shows one.
            var error = validator.Validate(Key, "Not/AZone");
            Assert.NotNull(error);
            Assert.Contains("IANA", error);
            Assert.Contains("America/Edmonton", error);
        }
    }

    // ── The live-reload rig (mirrors Story042/Story138/Story250's SeededProvider idiom) ─────────

    /// <summary>
    /// Testable <see cref="StationSettingsConfigurationProvider"/> subclass (Story042/Story138/
    /// Story250): overrides <see cref="Load"/> to seed the data bag from an in-memory dictionary
    /// instead of a real Postgres connection, so the SAME <see cref="Reload"/>/change-token path
    /// the real store exercises after a live write can be driven in-process, no DB required.
    /// </summary>
    sealed class SeededProvider : StationSettingsConfigurationProvider
    {
        readonly Dictionary<string, string?> seed;

        public SeededProvider(Dictionary<string, string?> seed) : base("Host=fakepg;")
        {
            this.seed = seed;
        }

        public override void Load()
        {
            foreach (var (key, value) in seed)
                Set(key, value);
        }
    }

    /// <summary>Thin IConfigurationSource that just returns an already-constructed provider.</summary>
    sealed class ProviderWrapperSource(IConfigurationProvider inner) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => inner;
    }

    /// <summary>
    /// Scriptable <see cref="IStationSettingsStore"/> that writes into the SAME seed dictionary the
    /// live <see cref="SeededProvider"/> reads from and calls its <see cref="SeededProvider.Reload"/> —
    /// the real config-reload seam <c>StationSettingsStore.WriteAsync</c> triggers after a real
    /// Postgres write, minus Postgres.
    /// </summary>
    sealed class SeededSettingsStore(Dictionary<string, string?> seed, SeededProvider provider)
        : IStationSettingsStore
    {
        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));
            seed[key] = value?.ToString() ?? string.Empty;
            provider.Reload();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string> result = seed
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// One live rig: a real <see cref="SettingsController"/> and a real
    /// <see cref="OptionsMonitorStationClockProvider"/> sharing the SAME configuration
    /// root/options monitor — a PUT through <see cref="Settings"/> is observed by
    /// <see cref="Clock"/> exactly as it would be in the running api, no restart.
    /// <see cref="Time"/> is the deterministic UTC source (and stands in for the container's own
    /// timezone via <see cref="FakeTimeProvider.SetLocalTimeZone"/>).
    /// </summary>
    sealed record LiveRig(SettingsController Settings, IStationClockProvider Clock, FakeTimeProvider Time);

    static LiveRig BuildLiveRig(DateTimeOffset utcNow, string? environmentTimezone = null)
    {
        var baseValues = new Dictionary<string, string?>
        {
            ["Station:Id"] = "s1",
            ["Station:Name"] = "GenWave",
            ["Station:Voice"] = "af_heart",
        };
        if (environmentTimezone is not null)
            baseValues[Key] = environmentTimezone;

        var seed = new Dictionary<string, string?>();
        var provider = new SeededProvider(seed);

        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(baseValues)
            .Add(new ProviderWrapperSource(provider))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(root);
        services.AddOptions<StationOptions>().Bind(root.GetSection(StationOptions.Section));
        var monitor = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<StationOptions>>();

        var time = new FakeTimeProvider(utcNow); // LocalTimeZone defaults to UTC — "the container"
        var clock = new OptionsMonitorStationClockProvider(monitor, time);
        var store = new SeededSettingsStore(seed, provider);

        var settingsController = new SettingsController(
            root, store, new SettingValidator(root), NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new LiveRig(settingsController, clock, time);
    }

    // A fixed winter instant: 20:00 UTC on 2026-01-10 is 13:00 in Edmonton (MST, UTC-7).
    static readonly DateTimeOffset WinterUtc = new(2026, 1, 10, 20, 0, 0, TimeSpan.Zero);

    // The same wall hour six months on: 20:00 UTC on 2026-07-10 is 14:00 in Edmonton (MDT, UTC-6).
    static readonly DateTimeOffset SummerUtc = new(2026, 7, 10, 20, 0, 0, TimeSpan.Zero);

    public sealed class ScenarioTheClockFollowsTheStation
    {
        [Fact]
        public void EmptyTimezoneKeepsTheContainersOwnClock()
        {
            // The fresh-deploy shape: no Station:Timezone anywhere — LocalNow is the container's
            // clock (the fake's LocalTimeZone), i.e. the pre-gh-#117 behavior unchanged.
            var rig = BuildLiveRig(WinterUtc);
            var container = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane"); // +10, no DST
            rig.Time.SetLocalTimeZone(container);

            var localNow = rig.Clock.LocalNow;

            Assert.Equal(TimeSpan.FromHours(10), localNow.Offset);
            Assert.Equal(new DateTime(2026, 1, 11, 6, 0, 0), localNow.DateTime);
        }

        [Fact]
        public async Task APutRepointsTheDjClockLiveAndConvertsAcrossDst()
        {
            var rig = BuildLiveRig(WinterUtc);

            var putResult = await rig.Settings.Put(
                [new SettingUpdateRequest(Key, "America/Edmonton")], CancellationToken.None);
            Assert.IsType<OkObjectResult>(putResult);

            // No api restart: the SAME IStationClockProvider instance now reads Edmonton wall
            // time — MST (UTC-7) in January...
            var winter = rig.Clock.LocalNow;
            Assert.Equal(TimeSpan.FromHours(-7), winter.Offset);
            Assert.Equal(new DateTime(2026, 1, 10, 13, 0, 0), winter.DateTime);

            // ...and MDT (UTC-6) in July, from the SAME stored id — the id is converted per read,
            // never frozen into an offset at write time.
            rig.Time.SetUtcNow(SummerUtc);
            var summer = rig.Clock.LocalNow;
            Assert.Equal(TimeSpan.FromHours(-6), summer.Offset);
            Assert.Equal(new DateTime(2026, 7, 10, 14, 0, 0), summer.DateTime);
        }

        [Fact]
        public async Task GarbageIsA400AndTheClockNeverMoves()
        {
            var rig = BuildLiveRig(WinterUtc);

            var putResult = await rig.Settings.Put(
                [new SettingUpdateRequest(Key, "Not/AZone")], CancellationToken.None);

            Assert.IsNotType<OkObjectResult>(putResult);
            Assert.Equal(TimeSpan.Zero, rig.Clock.LocalNow.Offset); // still the container's UTC clock
        }
    }

    public sealed class ScenarioAnUnresolvableZoneNeverFaultsThePatterPath
    {
        [Fact]
        public void EnvSuppliedGarbageFallsBackToTheContainersClock()
        {
            // The validator guards only the settings-API path — a compose env var can still hand
            // StationOptions.Timezone anything. A typo must degrade to the container's clock, not
            // throw into every prompt build / SegmentRequest stamp.
            var rig = BuildLiveRig(WinterUtc, environmentTimezone: "Not/AZone");

            var localNow = rig.Clock.LocalNow;

            Assert.Equal(TimeSpan.Zero, localNow.Offset);
            Assert.Equal(WinterUtc.DateTime, localNow.DateTime);
        }
    }
}
