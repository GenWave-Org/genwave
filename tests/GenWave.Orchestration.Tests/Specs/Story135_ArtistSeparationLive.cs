// STORY-135 — No same artist back to back (Epic V / SPEC F41.6, closes gitea-#213) — live-settings
// half. The catalog-query half lives in MediaLibrary.Tests/Specs/Story135_ArtistSeparationQuery.cs.
//
// BDD specification — xUnit. Implemented V4 (2026-07-14): the Orchestrator reads
// IRotationSettingsProvider.Current.ArtistSeparation fresh on every selection (mirrors F30.1/gitea-#211)
// and WARNs when the returned candidate carries RepeatedArtist.

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureArtistSeparationLive
{
    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static (Orchestrator Orchestrator, FakeMediaCatalog Catalog, FakeRotationSettingsProvider Rotation, CapturingLogger<Orchestrator> Logger)
        BuildOrchestrator(MediaReference? ready, int artistSeparation)
    {
        var catalog = new FakeMediaCatalog(ready);
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(SilentCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings { ArtistSeparation = artistSeparation });
        var logger = new CapturingLogger<Orchestrator>();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
            new FakeTtsSegmentSource(), new FakeActivePersonaAccessor(), logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System));
        return (orchestrator, catalog, rotationProvider, logger);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSeparationDepthIsLiveTunable
    {
        [Fact]
        public async Task TheProviderDepthIsPassedToEverySelection()
        {
            var (orchestrator, catalog, _, _) = BuildOrchestrator(MakeRef("m1"), artistSeparation: 3);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Equal([3, 3], catalog.RotationCallArtistSeparations);
        }

        [Fact]
        public async Task AChangedDepthAppliesOnTheVeryNextSelection()
        {
            var (orchestrator, catalog, rotation, _) = BuildOrchestrator(MakeRef("m1"), artistSeparation: 2);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.Equal(2, catalog.RotationCallArtistSeparations[0]);

            // The live edit: no re-construction, no restart — same provider instance, new value.
            rotation.Settings = rotation.Settings with { ArtistSeparation = 5 };

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.Equal(5, catalog.RotationCallArtistSeparations[1]);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioARelaxedArtistTierIsLogged
    {
        [Fact]
        public async Task ARepeatedArtistCandidateProducesAWarnNamingTheArtistRelaxation()
        {
            var (orchestrator, catalog, _, logger) = BuildOrchestrator(MakeRef("m1"), artistSeparation: 2);
            catalog.ScriptedRepeatedArtist = true;
            var ctx = new PlayoutContext([]);

            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(item);
            Assert.Contains(logger.Warnings, w =>
                w.Contains("artist", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("separation", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("relaxed", StringComparison.OrdinalIgnoreCase));
        }
    }
}
