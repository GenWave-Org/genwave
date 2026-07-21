// STORY-134 — Rotation never drains a playable catalog (Epic V / SPEC F41.5, closes gitea-#210) —
// diagnostics half. The catalog-query half lives in
// MediaLibrary.Tests/Specs/Story134_RotationNeverDrainsCatalogQuery.cs.
//
// BDD specification — xUnit. Implemented V4 (2026-07-14): the Orchestrator consumes
// GetRotationCandidateAsync (SPEC F41.1) and WARNs when a candidate carries RepeatedRecent or
// RepeatedArtist — the gitea-#210 "why did it drain" diagnostic becomes "why did it relax" — and WARNs
// naming the zero-playable pool on a genuine (F41.2) drain, which stays non-fatal (F6.3).

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureRotationRelaxationDiagnostics
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    // Silent cadence: GetNextAsync yields the music item first-slot with no TTS in the way, so
    // this file's facts can assert purely on selection/logging without a patter buffer to drain.
    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static (Orchestrator Orchestrator, FakeMediaCatalog Catalog, CapturingLogger<Orchestrator> Logger)
        BuildOrchestrator(MediaReference? ready)
    {
        var catalog = new FakeMediaCatalog(ready);
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(SilentCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var logger = new CapturingLogger<Orchestrator>();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
            new FakeTtsSegmentSource(), new FakeActivePersonaAccessor(), logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
        return (orchestrator, catalog, logger);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioARelaxedWindowIsFlaggedAndLogged
    {
        [Fact]
        public async Task ARepeatedRecentCandidateProducesAWarnNamingTheWindowRelaxation()
        {
            // The candidate ("m1") appears in the ordered-recent list handed to selection — the
            // fake catalog derives RepeatedRecent from orderedRecentIds.Contains(candidate.Id).
            var (orchestrator, _, logger) = BuildOrchestrator(MakeRef("m1"));
            var ctx = new PlayoutContext(["m1"]);

            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(item);
            Assert.Contains(logger.Warnings, w =>
                w.Contains("anti-repeat", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("relaxed", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task AnUnrelaxedCandidateProducesNoRelaxationWarn()
        {
            // The candidate is NOT in the recent list and RepeatedArtist stays false (default) —
            // no relaxation happened, so no WARN fires.
            var (orchestrator, _, logger) = BuildOrchestrator(MakeRef("m1"));
            var ctx = new PlayoutContext(["m2", "m3"]);

            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(item);
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public async Task TtsIdsAreStrippedFromTheOrderedRecentListBeforeSelection()
        {
            var (orchestrator, catalog, _) = BuildOrchestrator(MakeRef("m1"));
            var ctx = new PlayoutContext(["m2", "tts:abc", "m3"]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.DoesNotContain("tts:abc", catalog.RotationCallOrderedRecentIds[0]);
            Assert.All(
                catalog.RotationCallOrderedRecentIds[0],
                id => Assert.False(id.StartsWith("tts:", StringComparison.Ordinal)));
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioAGenuineDrainWarnsLoudly
    {
        [Fact]
        public async Task ANullSelectionProducesAWarnNamingTheZeroPlayablePool()
        {
            // ready == null ⇒ FakeMediaCatalog.GetRotationCandidateAsync returns null — the
            // zero-playable-pool case (F41.2), distinct from a relaxation.
            var (orchestrator, _, logger) = BuildOrchestrator(null);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("zero playable", warning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("relaxed", warning, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ANullSelectionRemainsNonFatalAndTheNextTickRetries()
        {
            var (orchestrator, catalog, _) = BuildOrchestrator(null);
            var ctx = new PlayoutContext([]);

            var first = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.Null(first);

            // The catalog is consulted again on the next tick — no stall, no exception (F6.3).
            var ex = await Record.ExceptionAsync(() => orchestrator.GetNextAsync(ctx, CancellationToken.None));
            Assert.Null(ex);
            Assert.Equal(2, catalog.RotationCallOrderedRecentIds.Count);
        }
    }
}
