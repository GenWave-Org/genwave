// STORY-074 — Safe-authoring config contract + `authored` volume
//
// BDD specification — xUnit. SPEC F27 config table + F11.12 + F27.10.
// StationOptions gains a Safe section (SeedMessage, AuthoredRoot, BedDuckDb,
// BedPadSeconds) with ValidateOnStart rules; compose.yaml gains the named
// `authored` volume (rw api / ro engine, no other service); Station:Safe:*
// stays OUT of the F19 live-settings allowlist. Zero behavior consumers
// until P5 — no service reads Safe.* yet.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeAuthoringConfigContractAndVolume
{
    // ── Shared helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="IOptionsMonitor{StationOptions}"/> from the given flat config values.
    /// Mirrors the Story054 SafeScope config-contract pattern: the caller supplies the
    /// complete effective config, no appsettings.json layer involved.
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

    /// <summary>Minimum config values unrelated to the Safe section under test.</summary>
    static Dictionary<string, string?> BaseConfig() => new()
    {
        ["Station:Id"]                 = "s1",
        ["Station:Name"]               = "GenWave",
        ["Station:Voice"]              = "af_heart",
        ["Station:Scope:LibraryIds:0"] = "1",
    };

    /// <summary>A minimally-valid StationOptions instance for direct validator construction.</summary>
    static StationOptions ValidOptions() => new()
    {
        Id    = "s1",
        Name  = "GenWave",
        Voice = "af_heart",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
    };

    /// <summary>
    /// Repo root, resolved relative to the test assembly's build output — mirrors the
    /// convention Story057 uses for engine/genwave.liq.
    /// </summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string ComposeText => File.ReadAllText(Path.Combine(RepoRoot, "compose.yaml"));

    /// <summary>
    /// Minimal line-based reader for compose.yaml's fixed 2-space indentation. No YamlDotNet
    /// dependency is pulled in for the two shapes asserted here: a top-level map's keys, and
    /// one service's volume mount list.
    /// </summary>
    static class ComposeYaml
    {
        public static IReadOnlyList<string> TopLevelKeysUnder(string text, string section)
        {
            var keys = new List<string>();
            var inSection = false;
            foreach (var line in Lines(text))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                if (IndentOf(line) == 0)
                {
                    inSection = trimmed == $"{section}:";
                    continue;
                }

                if (inSection && IndentOf(line) == 2 && trimmed.Contains(':'))
                    keys.Add(trimmed[..trimmed.IndexOf(':')]);
            }
            return keys;
        }

        public static IReadOnlyList<string> ServiceVolumeMounts(string text, string service)
        {
            var mounts = new List<string>();
            var inService = false;
            var inVolumes = false;
            foreach (var line in Lines(text))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var indent = IndentOf(line);

                if (indent == 2)
                {
                    inService = trimmed == $"{service}:";
                    inVolumes = false;
                    continue;
                }
                if (!inService)
                    continue;

                if (indent == 4)
                {
                    inVolumes = trimmed == "volumes:";
                    continue;
                }

                if (inVolumes && indent == 6 && trimmed.StartsWith("- "))
                    mounts.Add(StripInlineComment(trimmed[2..]).Trim());
            }
            return mounts;
        }

        /// <summary>Strips a trailing " # comment" from a compose.yaml list item value.</summary>
        static string StripInlineComment(string value)
        {
            var hashIndex = value.IndexOf('#');
            return hashIndex < 0 ? value : value[..hashIndex];
        }

        static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

        static int IndentOf(string line) => line.Length - line.TrimStart(' ').Length;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — Station:Safe options bind with defaults
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeOptionsBindWithDefaults
    {
        [Fact]
        public void SafePropertyExistsOnStationOptions()
        {
            // AC1 — typeof(StationOptions).GetProperty("Safe") is non-null.
            var prop = typeof(StationOptions).GetProperty(nameof(StationOptions.Safe));

            Assert.NotNull(prop);
            Assert.Equal(typeof(StationSafeOptions), prop.PropertyType);
        }

        [Fact]
        public void SeedMessageDefaultsToTheStandingByTemplate()
        {
            // AC1 — with no Station:Safe:* keys, Safe.SeedMessage ==
            //       "You're listening to {StationName}. We'll be right back — stay tuned."
            var monitor = BuildMonitor(BaseConfig());

            Assert.Equal(
                "You're listening to {StationName}. We'll be right back — stay tuned.",
                monitor.CurrentValue.Safe.SeedMessage);
        }

        [Fact]
        public void AuthoredRootDefaultsToSlashAuthored()
        {
            // AC1 — Safe.AuthoredRoot == "/authored"
            var monitor = BuildMonitor(BaseConfig());

            Assert.Equal("/authored", monitor.CurrentValue.Safe.AuthoredRoot);
        }

        [Fact]
        public void BedDuckDbDefaultsToMinusTwelve()
        {
            // AC1 — Safe.BedDuckDb == -12.0
            var monitor = BuildMonitor(BaseConfig());

            Assert.Equal(-12.0, monitor.CurrentValue.Safe.BedDuckDb);
        }

        [Fact]
        public void BedPadSecondsDefaultsToOnePointFive()
        {
            // AC1 — Safe.BedPadSeconds == 1.5
            var monitor = BuildMonitor(BaseConfig());

            Assert.Equal(1.5, monitor.CurrentValue.Safe.BedPadSeconds);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — compose topology (parse compose.yaml, no Docker started)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAuthoredVolumeInCompose
    {
        [Fact]
        public void VolumesMapContainsAuthored()
        {
            // AC2 — compose["volumes"] has key "authored"
            var volumes = ComposeYaml.TopLevelKeysUnder(ComposeText, "volumes");

            Assert.Contains("authored", volumes);
        }

        [Fact]
        public void ApiMountsAuthoredReadWrite()
        {
            // AC2 — api volumes contain "authored:/authored" (rw — no :ro suffix)
            var apiMounts = ComposeYaml.ServiceVolumeMounts(ComposeText, "api");

            Assert.Contains(apiMounts, m => m == "authored:/authored");
        }

        [Fact]
        public void EngineMountsAuthoredReadOnly()
        {
            // AC2 — engine volumes contain "authored:/authored:ro"
            var engineMounts = ComposeYaml.ServiceVolumeMounts(ComposeText, "engine");

            Assert.Contains(engineMounts, m => m == "authored:/authored:ro");
        }

        [Fact]
        public void NoOtherServiceMountsAuthored()
        {
            // AC2 — db, icecast, kokoro, admin_ui volume lists carry no "authored" entry
            foreach (var service in new[] { "db", "icecast", "kokoro", "admin_ui" })
            {
                var mounts = ComposeYaml.ServiceVolumeMounts(ComposeText, service);

                Assert.DoesNotContain(mounts, m => m.Contains("authored:", StringComparison.Ordinal));
            }
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — allowlist exclusion (F27.10) — passing pin, guards forever
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeKeysStayOutOfTheLiveSettingsAllowlist
    {
        [Fact]
        public void NoStationSafeKeyAppearsInTheAllowlist()
        {
            // AC3 — Station:Safe:* keys are generation-time inputs, not ear-tuning
            //       knobs (F27.10, the F26 lesson). This passes today and must
            //       KEEP passing after P1 registers the options section.
            //       GET /api/settings (SettingsController.Get) maps exactly over
            //       StationSettingsAllowlist.All, so this guards the endpoint too.
            Assert.DoesNotContain(
                StationSettingsAllowlist.ByKey.Keys,
                k => k.StartsWith("Station:Safe:", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — validation
    // ---------------------------------------------------------------------

    public sealed class ScenarioInvalidSafeValuesFailAtBoot
    {
        [Fact]
        public void BlankSeedMessageFailsIValidateOptions()
        {
            // AC4 — Safe.SeedMessage = "" fails with a message naming the key
            var validator = new StationOptionsValidator(NullLogger<StationOptionsValidator>.Instance);
            var options = ValidOptions();
            options.Safe.SeedMessage = "";

            var result = validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:Safe:SeedMessage",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void NegativeBedPadSecondsFailsIValidateOptions()
        {
            // AC4 — Safe.BedPadSeconds = -1 fails with a message naming the key
            var validator = new StationOptionsValidator(NullLogger<StationOptionsValidator>.Instance);
            var options = ValidOptions();
            options.Safe.BedPadSeconds = -1;

            var result = validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:Safe:BedPadSeconds",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }
    }
}
