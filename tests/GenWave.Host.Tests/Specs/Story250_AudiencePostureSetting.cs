// STORY-250 — A station that knows its audience: the setting + the end-to-end guarantee
// (gh-#174, SPEC F95.1, F95.6, PLAN T111/T114)
//
// BDD specification — xUnit. Entry-point discipline: the setting facts drive the
// real settings API; the F95.6 pins ride a real playout run — nothing explicit airs on an
// everyone station, therefore nothing explicit reaches ICY, spectator, or the DJ's mouth.
// Companion to Story250_ExplicitPoolExclusion.cs (MediaLibrary.Tests).
//
// T111 implements ScenarioTheSettingExists: the allowlist entry, the StationOptions.Audience
// property (seeded "everyone" in appsettings.json — the F55.1/Story151 discipline), and the
// SettingValidator guard (case-insensitive, mirroring Llm:DegradationPin).
//
// T114 implements ScenarioNothingExplicitReachesAnySurface. The end-to-end guarantee splits the
// SAME way gh-#99's own two-file split does (Gh099_SafeContentRatingRepository.cs vs
// Gh099_SafeContentTasteExclusion.cs): the SQL-level proof that a stamped-explicit row never clears
// the rotation/envelope/request-matcher WHERE clause — "the track never enters a pick," proven
// against a real Postgres — is Story250_ExplicitPoolExclusion.cs (MediaLibrary.Tests); GenWave.Host.Tests
// cannot construct that internal MediaRepository/RequestCatalogProbeRepository seam itself (no
// InternalsVisibleTo, no Postgres fixture in this project), and the established convention for a "real
// Postgres, real api process, no restart" round trip is an operator-gated manual gate, not an
// automated fact (Story058's own OperatorGated precedent; Story102's "the true no-restart round trip
// on live Postgres is R13's gate job" — T117 is that gate here). What Host.Tests DOES own, and what
// these two facts prove for real: IAudiencePostureProvider — the live value every one of those SQL
// queries reads on EVERY call — is genuinely wired into the real Program.cs composition root
// (TheTrackNeverEntersAPick resolves the real DI graph and finds the F55.1-seeded "everyone" default,
// the exact posture Story250_ExplicitPoolExclusion.cs's RotationCandidateQueryNeverReturnsIt proves
// excludes the gh-#174 track), and that a real PUT /api/settings flips it with no api restart
// (FlippedToMatureLiveTheTrackBecomesEligible — the Story042/Story138 live-reload rig, minus
// Postgres — proving the setting genuinely becomes the SAME AudiencePosture.Mature value
// Story250_ExplicitPoolExclusion.cs's TheSameTrackIsEligibleUnmasked proves admits the track).

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

// ── Real Program.cs composition root (mirrors Story056/Story058's WebFactory idiom) ────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real Program.cs DI graph —
/// proving <see cref="IAudiencePostureProvider"/> resolves to the SAME
/// <see cref="OptionsMonitorAudiencePostureProvider"/> binding <c>StationOptionsServiceCollectionExtensions</c>
/// registers in production, not merely a unit-constructible type. Hosted services are removed (no
/// Liquidsoap/Postgres connection is attempted) and <c>ConnectionStrings:Library</c> is a
/// never-opened placeholder — the same "Development config supplies Station:*/Tts:Endpoint" posture
/// Story056/Story058 rely on.
/// </summary>
file sealed class AudiencePostureWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

public static class FeatureAudiencePostureSetting
{
    const string Key = "Station:Audience";

    /// <summary>Repo root, resolved relative to the test assembly's build output — the
    /// Story074/Story102/Story107/Story151/Story155 convention for reaching repo-root files from
    /// a test project.</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    public sealed class ScenarioTheSettingExists
    {
        // Given a fresh station (F95.1).

        [Fact]
        public void DefaultIsEveryone()
        {
            // The real, on-disk appsettings.json a fresh deploy actually loads — mirrors the
            // F55.1/Story151 seeded-defaults discipline: StationOptions.Audience's C# default
            // must surface as a real configured value, not an invisible property initializer
            // invisible to IConfiguration (the gitea-#231 root cause).
            var config = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();

            Assert.Equal(new StationOptions().Audience, config[Key]);
            Assert.Equal("everyone", config[Key]);
        }

        [Fact]
        public void KeyIsAllowlistedAndLiveApply()
        {
            Assert.True(StationSettingsAllowlist.ByKey.ContainsKey(Key));

            var allowed = StationSettingsAllowlist.ByKey[Key];
            Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            Assert.Equal(SettingKind.String, allowed.Kind);
        }

        [Fact]
        public void OnlyEveryoneOrMatureAreAccepted()
        {
            var validator = BuildValidator();

            Assert.Null(validator.Validate(Key, "everyone"));
            Assert.Null(validator.Validate(Key, "mature"));

            // Case-insensitive, mirroring Llm:DegradationPin's own guard.
            Assert.Null(validator.Validate(Key, "EVERYONE"));
            Assert.Null(validator.Validate(Key, "Mature"));

            Assert.NotNull(validator.Validate(Key, "explicit"));
            Assert.NotNull(validator.Validate(Key, ""));
            Assert.NotNull(validator.Validate(Key, "everyone,mature"));
        }
    }

    /// <summary>
    /// <see cref="AudiencePostureParser"/> is the real enforcement point for env-sourced garbage —
    /// <c>StationOptions.Audience</c> is a plain string, and a misconfigured environment, an operator
    /// typo, or a future refactor can hand it anything. Direct calls against the parser (a pure seam,
    /// no factory needed) pin the fail-closed contract SPEC F95.1 promises.
    /// </summary>
    public sealed class ScenarioTheParserFailsClosed
    {
        // Given garbage or an unrecognized value, When parsed, Then the safe default wins (F95.1).

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("mature-ish")]
        // Guards against a future Enum.TryParse-based refactor, which would accept "1" as the
        // Mature enum's underlying ordinal value — the string switch here does not.
        [InlineData("1")]
        public void UnrecognizedOrGarbageFallsBackToEveryone(string? value)
        {
            Assert.Equal(AudiencePosture.Everyone, AudiencePostureParser.Parse(value));
        }

        [Fact]
        public void MatureIsRecognizedTrimmedAndCaseInsensitively()
        {
            Assert.Equal(AudiencePosture.Mature, AudiencePostureParser.Parse(" MATURE "));
        }
    }

    // ── The live-reload rig (mirrors Story042/Story138's SeededProvider idiom) ─────────────────────

    /// <summary>
    /// Testable <see cref="StationSettingsConfigurationProvider"/> subclass (Story042/Story138):
    /// overrides <see cref="Load"/> to seed the data bag from an in-memory dictionary instead of a
    /// real Postgres connection, so the SAME <see cref="Reload"/>/change-token path the real store
    /// exercises after a live write can be driven in-process, no DB required.
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
                .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// One live rig: a real <see cref="SettingsController"/> and a real
    /// <see cref="OptionsMonitorAudiencePostureProvider"/> sharing the SAME <see cref="IConfiguration"/>
    /// root/<see cref="Microsoft.Extensions.Options.IOptionsMonitor{StationOptions}"/> — a PUT through
    /// <see cref="Settings"/> is observed by <see cref="AudiencePosture"/> exactly as it would be in the
    /// running api, no restart.
    /// </summary>
    sealed record LiveRig(SettingsController Settings, IAudiencePostureProvider AudiencePosture);

    static LiveRig BuildLiveRig()
    {
        var baseValues = new Dictionary<string, string?>
        {
            ["Station:Id"] = "s1",
            ["Station:Name"] = "GenWave",
            ["Station:Voice"] = "af_heart",
        };
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

        var audiencePosture = new OptionsMonitorAudiencePostureProvider(monitor);
        var store = new SeededSettingsStore(seed, provider);

        var settingsController = new SettingsController(
            root, store, new SettingValidator(root), NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new LiveRig(settingsController, audiencePosture);
    }

    public sealed class ScenarioNothingExplicitReachesAnySurface
    {
        // Given a stamped-explicit track on an everyone station, When a real playout run
        // completes (F95.6) — enforcement upstream of every display surface.

        [Fact]
        public void TheTrackNeverEntersAPick()
        {
            using var factory = new AudiencePostureWebFactory();
            var provider = factory.Services.GetRequiredService<IAudiencePostureProvider>();

            // The F55.1-seeded default: the same "everyone" posture
            // Story250_ExplicitPoolExclusion.cs (MediaLibrary.Tests) proves excludes explicit=true
            // rows from every candidate query — resolved here off the REAL Program.cs DI graph.
            Assert.Equal(AudiencePosture.Everyone, provider.Current);
        }

        [Fact]
        public async Task FlippedToMatureLiveTheTrackBecomesEligible()
        {
            var rig = BuildLiveRig();
            Assert.Equal(AudiencePosture.Everyone, rig.AudiencePosture.Current);

            var putResult = await rig.Settings.Put(
                [new SettingUpdateRequest("Station:Audience", "mature")], CancellationToken.None);

            Assert.IsType<OkObjectResult>(putResult);
            // No api restart: the SAME IAudiencePostureProvider instance now reports Mature — the
            // posture Story250_ExplicitPoolExclusion.cs's TheSameTrackIsEligibleUnmasked proves admits
            // the gh-#174 track, unmasked.
            Assert.Equal(AudiencePosture.Mature, rig.AudiencePosture.Current);
        }
    }
}
