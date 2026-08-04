// STORY-265 — Provider ordering guard (PLAN T165, precondition carried from T164's review)
//
// BDD specification — xUnit. StationSettingsHostingExtensions.AddGenWaveStationSettings registers
// the station.settings DB overlay AFTER AddEnvironmentVariables() specifically so a saved settings
// row wins over an env/appsettings default (SPEC F102.5) — config is last-wins, and that ordering is
// what lets ThemeCatalog.Resolve take a single already-merged stationSlug argument instead of
// separately-ranked env/settings-row ones. Nothing pinned that order before this spec:
// Story042_StationSettingsOverlayProvider tests the overlay-over-appsettings CONTRACT with a fake
// provider seeded directly, and Story265_ThemeSelectionAndPersistence's own AC2/AC3 specs model the
// layering with two hand-built IConfiguration providers in the same order Program.cs uses — both
// prove the SEMANTICS of "later wins", never that the REAL StationSettingsHostingExtensions method
// actually registers its two sources in that order. Silently reversing its two lines would invert
// F102.5's precedence — an env default beating a saved settings row — with nothing going red.
//
// This spec calls the real production extension method and inspects the real
// IConfigurationBuilder.Sources it produces, so it fails if the two registrations are swapped.
// Verified by hand at T165: swapping StationSettingsHostingExtensions.cs's two lines turns this red
// (confirmed, then reverted) — see the task's own report for the transcript.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationSettingsProviderOrderingGuard
{
    public sealed class ScenarioTheDbOverlayIsRegisteredAfterEnvironmentVariables
    {
        [Fact]
        public void TheStationSettingsSourceIsLastAndAnEnvironmentSourceImmediatelyPrecedesIt()
        {
            // Arrange: a real WebApplicationBuilder — the exact type Program.cs builds — with no
            //          Station connection string configured. That is the safe, instant "local
            //          dev/tests" path StationSettingsConfigurationProvider.Load already documents
            //          (an empty connection string short-circuits before any Postgres attempt), so
            //          this spec needs no database.
            var builder = WebApplication.CreateBuilder();

            // Act: the REAL production extension method — not a reimplementation of its ordering.
            builder.AddGenWaveStationSettings();

            // Assert: the station.settings overlay source is the LAST configuration source this
            //         method (or anything before it) registered, and an
            //         EnvironmentVariablesConfigurationSource immediately precedes it. That shape is
            //         only true when AddEnvironmentVariables() runs BEFORE
            //         Configuration.Sources.Add(stationSettingsSource) — StationSettingsHostingExtensions'
            //         own two lines, in that order. Reversing them makes the LAST source an
            //         EnvironmentVariablesConfigurationSource instead of the DB overlay, and this
            //         assertion goes red.
            var sources = builder.Configuration.Sources;
            Assert.IsType<StationSettingsConfigurationSource>(sources[^1]);
            Assert.IsType<EnvironmentVariablesConfigurationSource>(sources[^2]);
        }
    }
}
