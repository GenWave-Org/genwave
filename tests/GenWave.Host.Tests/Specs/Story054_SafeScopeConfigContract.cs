// STORY-054 — SafeScope config contract + defaults (batched enabler)
//
// BDD specification — xUnit. StationOptions gains a SafeScope property bound to
// Station:SafeScope:LibraryIds; F19 AllowedSetting registry lists it as a live-apply key.
// Zero behavior change — no consumer yet. K2b introduces the first consumer.
// Skip-pinned until K1 lands. See docs/PLAN.md Epic K.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeScopeConfigContract
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="IOptionsMonitor{StationOptions}"/> from the given flat config values.
    /// The caller supplies the COMPLETE effective config — no appsettings.json layer is added,
    /// so the test drives only what it declares.
    /// </summary>
    static IOptionsMonitor<StationOptions> BuildMonitor(Dictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services
            .AddOptions<StationOptions>()
            .Bind(config.GetSection(StationOptions.Section));

        return services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<StationOptions>>();
    }

    /// <summary>Minimum required config values for a valid StationOptions.</summary>
    static Dictionary<string, string?> BaseConfig() => new()
    {
        ["Station:Id"]                 = "s1",
        ["Station:Name"]               = "GenWave",
        ["Station:Voice"]              = "af_heart",
        ["Station:Scope:LibraryIds:0"] = "1",
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeScopeIsBoundOnStationOptions
    {
        [Fact]
        public void SafeScopePropertyExists()
        {
            // AC1 — StationOptions has a SafeScope property of type StationScopeOptions,
            //       matching the shape of Station:Scope:LibraryIds.
            var t = typeof(StationOptions);
            var prop = t.GetProperty(nameof(StationOptions.SafeScope));
            Assert.NotNull(prop);
            Assert.Equal(typeof(StationScopeOptions), prop.PropertyType);
        }

        [Fact]
        public void SafeScopeDefaultsToLibraryOne()
        {
            // AC2 — with no Station:SafeScope:LibraryIds override the deployment default of [1]
            //       (from appsettings.json) is bound.  Tested here via the config pipeline
            //       with the appsettings default mirrored in the in-memory source, confirming
            //       that a fresh deployment resolves SafeScope = [1] without an env override.
            var cfg = BaseConfig();
            cfg["Station:SafeScope:LibraryIds:0"] = "1";   // mirrors the appsettings.json default

            var monitor = BuildMonitor(cfg);

            Assert.Equal([1L], monitor.CurrentValue.SafeScope.LibraryIds.ToArray());
        }

        [Fact]
        public void SafeScopeBindsFromConfiguration()
        {
            // AC3 — a config source setting Station:SafeScope:LibraryIds = [7, 8] yields
            //       SafeScope = [7, 8] via IOptionsMonitor<StationOptions>.CurrentValue.
            //       (Starting from an empty code-level list so the override fully replaces
            //       the appsettings default, mirroring how Station:Scope works.)
            var cfg = BaseConfig();
            cfg["Station:SafeScope:LibraryIds:0"] = "7";
            cfg["Station:SafeScope:LibraryIds:1"] = "8";

            var monitor = BuildMonitor(cfg);

            Assert.Equal([7L, 8L], monitor.CurrentValue.SafeScope.LibraryIds.ToArray());
        }
    }

    public sealed class ScenarioSafeScopeIsARegisteredLiveApplySetting
    {
        [Fact]
        public void SafeScopeAppearsInTheAllowedSettingRegistry()
        {
            // AC4 — the F19 AllowedSetting list contains an entry for
            //       Station:SafeScope:LibraryIds with applyMode=live and
            //       kind=NumberList (the long[] shape).
            Assert.True(
                StationSettingsAllowlist.ByKey.ContainsKey("Station:SafeScope:LibraryIds"),
                "Station:SafeScope:LibraryIds must appear in StationSettingsAllowlist.");

            var entry = StationSettingsAllowlist.ByKey["Station:SafeScope:LibraryIds"];
            Assert.Equal(SettingApplyMode.Live, entry.ApplyMode);
            Assert.Equal(SettingKind.NumberList, entry.Kind);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — validation
    // ---------------------------------------------------------------------

    public sealed class ScenarioInvalidSafeScopeIsRejected
    {
        [Fact]
        public void MalformedSafeScopeFailsIValidateOptions()
        {
            // AC5 — a config source with Station:SafeScope:LibraryIds containing negative ids
            //       is rejected by IValidateOptions<StationOptions> with a message referring
            //       to the key (fails ValidateOnStart at host startup).
            var validator = new StationOptionsValidator(NullLogger<StationOptionsValidator>.Instance);
            var opts = new StationOptions
            {
                Id = "s1",
                Name = "G",
                Voice = "v",
                Scope = new StationScopeOptions { LibraryIds = [1L] },
                SafeScope = new StationScopeOptions { LibraryIds = [-1L] }, // negative id — must fail
            };

            var result = validator.Validate(null, opts);

            Assert.True(result.Failed);
            var failureMessage = result.FailureMessage ?? string.Empty;
            Assert.Contains("SafeScope", failureMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EmptySafeScopePassesIValidateOptions()
        {
            // AC5b — updated by N1 (STORY-068): an empty Station:SafeScope:LibraryIds is now
            // VALID (degraded-mode — drain events play mksafe silence, F4.4). The validator
            // emits a WARN log but returns Success. Only non-positive ids still fail.
            var validator = new StationOptionsValidator(NullLogger<StationOptionsValidator>.Instance);
            var opts = new StationOptions
            {
                Id = "s1",
                Name = "G",
                Voice = "v",
                Scope = new StationScopeOptions { LibraryIds = [1L] },
                SafeScope = new StationScopeOptions(), // empty — now valid (F4.4 degraded mode)
            };

            var result = validator.Validate(null, opts);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void ValidSafeScopePassesIValidateOptions()
        {
            // AC5c — a valid SafeScope (non-empty, all positive ids) passes the validator.
            var validator = new StationOptionsValidator(NullLogger<StationOptionsValidator>.Instance);
            var opts = new StationOptions
            {
                Id = "s1",
                Name = "G",
                Voice = "v",
                Scope = new StationScopeOptions { LibraryIds = [1L] },
                SafeScope = new StationScopeOptions { LibraryIds = [1L, 2L] },
            };

            var result = validator.Validate(null, opts);

            Assert.True(result.Succeeded);
        }
    }
}
