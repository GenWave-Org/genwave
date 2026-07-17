// STORY-138 — Station identity goes live (Epic V / SPEC F44.1 + F44.5–F44.6, closes gitea-#196; gitea-#195's
// wordmark half lives in admin-ui/__specs__/station-wordmark.spec.tsx).
//
// The Orchestrator-side "next patter segment carries the edited name" fact (AC2) lives in
// Orchestration.Tests/Specs/Story138_StationIdentityLive.cs instead of here — the Story131 split
// precedent (facts live where their subject compiles: Orchestrator is Orchestration-project code).
// It splits into two facts there (baseline + live-edit), mirroring Story135's
// TheProviderDepthIsPassedToEverySelection/AChangedDepthAppliesOnTheVeryNextSelection pair — hence
// this file's 7 facts + Orchestration.Tests' 2 == the 9 STORY-138 facts.
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-14, house rule since Epic S).
// Implemented V7 (2026-07-14).
//
// The live-reload seam mirrors Story042's SeededProvider idiom (a StationSettingsConfigurationProvider
// subclass with Load() overridden to seed from an in-memory dictionary instead of Postgres) plus a
// scriptable IStationSettingsStore that writes into that SAME dictionary and calls Reload() — the
// exact change-token path the real StationSettingsStore.WriteAsync triggers after a live Postgres
// write, minus Postgres. A real SettingsController.Put() and a real IStationIdentityProvider share
// the ONE resulting IConfiguration root/IOptionsMonitor<StationOptions>, so a PUT through the
// controller is observed by the provider exactly as it would be in the running api (Story102/
// Story124's "prove IOptionsMonitor re-binds" idiom, applied to identity).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Auth;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
// Alias to disambiguate from the GenWave.Host.Options namespace (mirrors Story012's precedent).
using ExtOptions = Microsoft.Extensions.Options.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationIdentityLive
{
    // ── The scriptable config-reload seam (mirrors Story042's SeededProvider) ──────────────────

    /// <summary>
    /// Testable <see cref="StationSettingsConfigurationProvider"/> subclass (mirrors Story042's
    /// SeededProvider): overrides <see cref="Load"/> to seed the data bag from an in-memory
    /// dictionary instead of a real Postgres connection, so the SAME <see cref="Reload"/> /
    /// change-token path the real store exercises after a live Postgres write can be driven
    /// in-process, no DB required.
    /// </summary>
    sealed class SeededProvider : StationSettingsConfigurationProvider
    {
        readonly Dictionary<string, string?> seed;

        public SeededProvider(Dictionary<string, string?> seed)
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

    /// <summary>Thin IConfigurationSource that just returns an already-constructed provider.</summary>
    sealed class ProviderWrapperSource(IConfigurationProvider inner) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => inner;
    }

    /// <summary>
    /// Scriptable <see cref="IStationSettingsStore"/> that writes into the SAME seed dictionary the
    /// live <see cref="SeededProvider"/> reads from and calls its <see cref="SeededProvider.Reload"/>
    /// — the real config-reload seam <c>StationSettingsStore.WriteAsync</c> triggers after a real
    /// Postgres write, minus Postgres. This is what makes a PUT through <see cref="SettingsController"/>
    /// genuinely reach a live <see cref="IOptionsMonitor{StationOptions}"/> re-bind rather than
    /// merely returning 200.
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
    /// <see cref="OptionsMonitorStationIdentityProvider"/>/<see cref="AuthController"/> sharing the
    /// SAME <see cref="IConfiguration"/> root and <see cref="IOptionsMonitor{StationOptions}"/> — a
    /// PUT through <see cref="Settings"/> is observed by <see cref="IdentityProvider"/>/
    /// <see cref="Auth"/> exactly as it would be in the running api, no restart.
    /// </summary>
    sealed record LiveRig(SettingsController Settings, IStationIdentityProvider IdentityProvider, AuthController Auth);

    static LiveRig BuildLiveRig(string name = "GenWave", string voice = "af_heart")
    {
        var baseValues = new Dictionary<string, string?>
        {
            ["Station:Id"] = "s1",
            ["Station:Name"] = name,
            ["Station:Voice"] = voice,
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
        var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<StationOptions>>();

        var identityProvider = new OptionsMonitorStationIdentityProvider(monitor);
        var store = new SeededSettingsStore(seed, provider);

        var settingsController = new SettingsController(
            root, store, new SettingValidator(root), NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var authController = new AuthController(
            ExtOptions.Create(new AdminOptions()), identityProvider, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new LiveRig(settingsController, identityProvider, authController);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNameEditsApplyLive
    {
        [Fact]
        public async Task ApiStationsReturnsTheLiveEffectiveName()
        {
            // AC1/F44.6 — a Station:Name PUT is visible on the very next GET /api/stations call,
            // no api restart.
            var rig = BuildLiveRig(name: "GenWave");

            var before = Assert.IsType<OkObjectResult>(rig.Auth.Stations());
            var beforeDto = Assert.IsAssignableFrom<IEnumerable<StationDto>>(before.Value).Single();
            Assert.Equal("GenWave", beforeDto.Name);

            var putResult = await rig.Settings.Put(
                [new SettingUpdateRequest("Station:Name", "Radio Free Somewhere")], CancellationToken.None);
            Assert.IsType<OkObjectResult>(putResult);

            var after = Assert.IsType<OkObjectResult>(rig.Auth.Stations());
            var afterDto = Assert.IsAssignableFrom<IEnumerable<StationDto>>(after.Value).Single();
            Assert.Equal("Radio Free Somewhere", afterDto.Name);
        }

        [Fact]
        public async Task TheNextDefaultVoiceRenderUsesTheEditedStationVoice()
        {
            // AC5/F44.2 — the Orchestrator's default-voice fallback is a trivial pass-through of
            // IStationIdentityProvider.Current.Voice (the fallback LOGIC is independently proven by
            // Story121's persona-voice-resolution facts); this proves the LIVE SEAM itself: a
            // Station:Voice PUT reaches the very next .Current read with no api restart, exactly
            // like Name above — so the very next default-voice render (whichever segment reads it
            // next) sees the edit.
            var rig = BuildLiveRig(voice: "af_heart");
            Assert.Equal("af_heart", rig.IdentityProvider.Current.Voice);

            var putResult = await rig.Settings.Put(
                [new SettingUpdateRequest("Station:Voice", "am_onyx")], CancellationToken.None);
            Assert.IsType<OkObjectResult>(putResult);

            Assert.Equal("am_onyx", rig.IdentityProvider.Current.Voice);
        }

        [Fact]
        public void StationContextNoLongerExistsInTheHostAssembly()
        {
            // AC6/F44.1 — repo-content assertion: the retired singleton no longer exists anywhere
            // in the Core assembly (reflection over the assembly StationIdentity now lives in).
            var coreAssembly = typeof(GenWave.Core.Domain.StationIdentity).Assembly;
            Assert.Null(coreAssembly.GetType("GenWave.Core.Domain.StationContext"));
        }
    }

    public sealed class ScenarioTheAllowlistCarriesIdentityHonestly
    {
        [Fact]
        public void StationNameIsAllowlistedLive()
        {
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue("Station:Name", out var entry));
            Assert.Equal(SettingApplyMode.Live, entry.ApplyMode);
            Assert.Equal(SettingKind.String, entry.Kind);
        }

        [Fact]
        public void StationVoiceIsAllowlistedLive()
        {
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue("Station:Voice", out var entry));
            Assert.Equal(SettingApplyMode.Live, entry.ApplyMode);
            Assert.Equal(SettingKind.String, entry.Kind);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioABlankNameIsRejected
    {
        [Fact]
        public async Task AnEmptyStationNamePutIs400()
        {
            // AC7/F19.5 — an empty Station:Name PUT is rejected, not persisted.
            var rig = BuildLiveRig();

            var result = await rig.Settings.Put(
                [new SettingUpdateRequest("Station:Name", "")], CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task NothingIsPersistedOnAFailedNameValidation()
        {
            // AC7 — the running station never observes the rejected value: the live-effective name
            // is unchanged after the failed PUT.
            var rig = BuildLiveRig(name: "GenWave");

            await rig.Settings.Put([new SettingUpdateRequest("Station:Name", "")], CancellationToken.None);

            Assert.Equal("GenWave", rig.IdentityProvider.Current.Name);
        }
    }
}
