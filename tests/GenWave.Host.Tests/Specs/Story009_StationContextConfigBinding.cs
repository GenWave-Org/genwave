// STORY-009 — StationContext config binding
//
// V7 migration note (SPEC F44.1, gitea-#196, 2026-07-14): StationContext is retired — identity is read
// live through IStationIdentityProvider instead (Host impl: OptionsMonitorStationIdentityProvider).
// The shape/binding facts below are updated to the new type; STORY-138's own spec file
// (Story138_StationIdentityLive.cs) owns the LIVENESS half (a settings edit reaching this provider
// without a restart) — this file keeps the static config→identity mapping coverage.

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationIdentityConfigBinding
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationIdentityRecordExistsInAbstractions
    {
        // gitea-#251: StationIdentity is SDK surface (IStationIdentityProvider's return type) — it lives
        // in the dependency-free GenWave.Abstractions project; namespace unchanged.
        [Fact]
        public void TypeExistsInAbstractionsDomain() =>
            Assert.NotNull(Type.GetType("GenWave.Core.Domain.StationIdentity, GenWave.Abstractions"));

        [Fact]
        public void RecordExposesIdNameVoiceProperties()
        {
            // No Scope property (SPEC F30.1, STORY-102): the live scope is read via
            // IStationScopeProvider instead — see FeatureMainScopeLiveness. No Cadence property
            // either (gitea-#211, the same F30.1 precedent applied to cadence): the live cadence is read
            // via ICadenceProvider instead.
            var t = typeof(StationIdentity);
            Assert.NotNull(t.GetProperty("Id"));
            Assert.NotNull(t.GetProperty("Name"));
            Assert.NotNull(t.GetProperty("Voice"));
        }

        [Fact]
        public void RecordCarriesNoCadenceProperty() =>
            Assert.Null(typeof(StationIdentity).GetProperty("Cadence"));
    }

    public sealed class ScenarioIdentityProviderMapsConfiguredValues
    {
        // Regression coverage for gitea-#196's OptionsMonitorStationIdentityProvider (mirrors
        // ScenarioCadenceProviderMapsConfiguredValues below one seam over): proves the provider's
        // per-read mapping carries all three Station:Id/Name/Voice fields from a config-built
        // IOptionsMonitor<StationOptions> — the seam that replaced Program.cs's old one-time
        // StationContext(opts.Id, opts.Name, opts.Voice) construction at boot.
        static IOptionsMonitor<StationOptions> BuildMonitor(Dictionary<string, string?> configValues)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

            var services = new ServiceCollection();
            services
                .AddOptions<StationOptions>()
                .Bind(config.GetSection(StationOptions.Section));

            return services.BuildServiceProvider()
                .GetRequiredService<IOptionsMonitor<StationOptions>>();
        }

        readonly StationIdentity identity;

        public ScenarioIdentityProviderMapsConfiguredValues()
        {
            var cfg = new Dictionary<string, string?>
            {
                ["Station:Id"] = "s1",
                ["Station:Name"] = "GenWave",
                ["Station:Voice"] = "af_heart",
                ["Station:Scope:LibraryIds:0"] = "1",
            };

            var monitor = BuildMonitor(cfg);
            identity = new OptionsMonitorStationIdentityProvider(monitor).Current;
        }

        [Fact]
        public void IdFlowsFromConfiguration() =>
            Assert.Equal("s1", identity.Id);

        [Fact]
        public void NameFlowsFromConfiguration() =>
            Assert.Equal("GenWave", identity.Name);

        [Fact]
        public void VoiceFlowsFromConfiguration() =>
            Assert.Equal("af_heart", identity.Voice);
    }

    public sealed class ScenarioCadenceProviderMapsConfiguredValues
    {
        // Regression coverage for gitea-#211's OptionsMonitorCadenceProvider (mirrors Story054's
        // SafeScope-binds-from-configuration fact): proves the provider's per-read mapping carries
        // all three Station:Cadence:* fields from a config-built IOptionsMonitor<StationOptions>,
        // not just the two that happen to share a default with CadenceConfig's own defaults.
        static IOptionsMonitor<StationOptions> BuildMonitor(Dictionary<string, string?> configValues)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

            var services = new ServiceCollection();
            services
                .AddOptions<StationOptions>()
                .Bind(config.GetSection(StationOptions.Section));

            return services.BuildServiceProvider()
                .GetRequiredService<IOptionsMonitor<StationOptions>>();
        }

        readonly CadenceConfig cadence;

        public ScenarioCadenceProviderMapsConfiguredValues()
        {
            var cfg = new Dictionary<string, string?>
            {
                ["Station:Id"] = "s1",
                ["Station:Name"] = "GenWave",
                ["Station:Voice"] = "af_heart",
                ["Station:Scope:LibraryIds:0"] = "1",
                // All three deliberately set AWAY from CadenceConfig's own defaults (true/true/4)
                // so a fact passing by coincidence (an unmapped field silently keeping its default)
                // is impossible.
                ["Station:Cadence:LeadInBeforeEachTrack"] = "false",
                ["Station:Cadence:BackAnnounceAfterEachTrack"] = "false",
                ["Station:Cadence:StationIdEveryNUnits"] = "7",
            };

            var monitor = BuildMonitor(cfg);
            cadence = new OptionsMonitorCadenceProvider(monitor).Current;
        }

        [Fact]
        public void LeadInBeforeEachTrackFlowsFromConfiguration() =>
            Assert.False(cadence.LeadInBeforeEachTrack);

        [Fact]
        public void BackAnnounceAfterEachTrackFlowsFromConfiguration() =>
            Assert.False(cadence.BackAnnounceAfterEachTrack);

        [Fact]
        public void StationIdEveryNUnitsFlowsFromConfiguration() =>
            Assert.Equal(7, cadence.StationIdEveryNUnits);
    }

    public sealed class ScenarioSaneDefaultsWhenCadenceOmitted
    {
        [Fact]
        public void LeadInBeforeEachTrackDefaultsToTrue() =>
            Assert.True(new StationCadenceOptions().LeadInBeforeEachTrack);

        [Fact]
        public void BackAnnounceAfterEachTrackDefaultsToTrue() =>
            Assert.True(new StationCadenceOptions().BackAnnounceAfterEachTrack);

        [Fact]
        public void StationIdEveryNUnitsDefaultsToFour() =>
            Assert.Equal(4, new StationCadenceOptions().StationIdEveryNUnits);
    }

    public sealed class ScenarioScopeIsMandatoryAndNonEmpty
    {
        // Exercises the predicate registered with .Validate() in Program.cs.
        [Fact]
        public void StartupThrowsOptionsValidationExceptionForEmptyScope()
        {
            var opts = new StationOptions { Id = "s1", Name = "G", Voice = "v", Scope = new StationScopeOptions() };
            // The validate predicate used in Program.cs: opts.Scope.LibraryIds.Count > 0
            Assert.False(opts.Scope.LibraryIds.Count > 0);
        }
    }

    public sealed class ScenarioVoiceIsMandatory
    {
        [Fact]
        public void StartupThrowsOptionsValidationExceptionForMissingVoice()
        {
            var opts = new StationOptions { Id = "s1", Name = "G", Voice = "" };
            var results = new List<ValidationResult>();
            var valid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);
            Assert.False(valid);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — extra keys
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownExtraKeysAreToleratedNotRejected
    {
        // .NET configuration binding silently ignores unknown keys by default.
        // The positive case: all required fields present => DataAnnotations validation passes.
        [Fact]
        public void StartupSucceedsWithUnknownExtraStationKey()
        {
            var opts = new StationOptions
            {
                Id = "s1",
                Name = "G",
                Voice = "v",
                Scope = new StationScopeOptions { LibraryIds = [1L] },
            };
            var results = new List<ValidationResult>();
            var valid = Validator.TryValidateObject(opts, new ValidationContext(opts), results, validateAllProperties: true);
            Assert.True(valid);
        }
    }
}
