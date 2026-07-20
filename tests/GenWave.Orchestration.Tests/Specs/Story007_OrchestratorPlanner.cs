// STORY-007 — Orchestrator planner (interleave + cadence + stamped ids + TTS-id filter)

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureOrchestratorPlanner
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

    static Orchestrator BuildOrchestrator(
        IMediaCatalog catalog,
        ITtsSegmentSource ttsSource,
        CadenceConfig? cadence = null,
        LibraryScope? scope = null,
        TimeSpan? renderBudget = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(scope ?? new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence ?? new CadenceConfig());
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, ttsSource,
            new FakeActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(renderBudget ?? TimeSpan.FromSeconds(30)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
    }

    static List<MediaItem> ProduceN(Orchestrator orchestrator, int n)
    {
        var ctx = new PlayoutContext([]);
        var result = new List<MediaItem>(n);
        for (int i = 0; i < n; i++)
        {
            var item = orchestrator.GetNextAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotNull(item);
            result.Add(item);
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // HAPPY PATH
    // -------------------------------------------------------------------------

    public sealed class ScenarioImplementsINextItemProvider
    {
        [Fact]
        public void OrchestratorImplementsINextItemProvider() =>
            Assert.True(typeof(INextItemProvider).IsAssignableFrom(typeof(Orchestrator)));

        [Fact]
        public void GetNextAsyncReturnsTaskOfNullableMediaItem()
        {
            var m = typeof(Orchestrator).GetMethod("GetNextAsync");
            Assert.NotNull(m);
            Assert.Equal(typeof(Task<MediaItem?>), m.ReturnType);
        }
    }

    public sealed class ScenarioInterleavesMusicAndPatterPerCadenceConfig
    {
        // Sequence for 15 items with LeadIn=true, BackAnnounce=true, StationIdEveryN=4. STORY-136
        // (SPEC F42.1) added unitCount > 0 to the station-id guard, so the first ID no longer
        // fires at unit 0 (boot) — it now fires once unitCount reaches N, i.e. at unit 4:
        //
        //   Unit 0 (unitCount=0 before increment): no prev → no BackAnnounce;
        //     unitCount==0 fails unitCount>0 → no StationId; LeadIn; Music
        //   Unit 1 (unitCount=1): BackAnnounce; 1%4≠0 no StationId; LeadIn; Music
        //   Unit 2 (unitCount=2): BackAnnounce; LeadIn; Music
        //   Unit 3 (unitCount=3): BackAnnounce; LeadIn; Music
        //   Unit 4 (unitCount=4): BackAnnounce; 4>0 && 4%4==0 → StationId; LeadIn; Music
        //
        //   Indices: [0]=LeadIn [1]=Music
        //            [2]=BackAnnounce [3]=LeadIn [4]=Music
        //            [5]=BackAnnounce [6]=LeadIn [7]=Music
        //            [8]=BackAnnounce [9]=LeadIn [10]=Music
        //            [11]=BackAnnounce [12]=StationId [13]=LeadIn [14]=Music

        readonly List<MediaItem> produced;

        public ScenarioInterleavesMusicAndPatterPerCadenceConfig()
        {
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = true,
                StationIdEveryNUnits = 4,
            };
            var catalog = new FakeMediaCatalog(MakeRef("track1"));
            var tts = new FakeTtsSegmentSource();
            produced = ProduceN(BuildOrchestrator(catalog, tts, cadence), 15);
        }

        [Fact]
        public void EveryMusicTrackIsPrecededByALeadIn()
        {
            for (int i = 1; i < produced.Count; i++)
            {
                if (produced[i].MediaId.StartsWith("tts:", StringComparison.Ordinal)) continue;

                Assert.True(
                    produced[i - 1].MediaId.StartsWith("tts:leadin", StringComparison.OrdinalIgnoreCase),
                    $"Music at [{i}] ({produced[i].MediaId}) expected tts:leadin before it, got [{i - 1}] = {produced[i - 1].MediaId}");
            }
        }

        [Fact]
        public void EveryMusicTrackExceptLastIsFollowedByABackAnnounce()
        {
            var musicIndices = Enumerable.Range(0, produced.Count)
                .Where(i => !produced[i].MediaId.StartsWith("tts:", StringComparison.Ordinal))
                .ToList();

            // Skip the last music item — no item follows it in our 12-item window
            foreach (var idx in musicIndices.SkipLast(1))
            {
                Assert.True(
                    produced[idx + 1].MediaId.StartsWith("tts:backannounce", StringComparison.OrdinalIgnoreCase),
                    $"Music at [{idx}] expected tts:backannounce after it, got [{idx + 1}] = {produced[idx + 1].MediaId}");
            }
        }

        [Fact]
        public void ExactlyOneStationIdOnceUnitCountReachesFour()
        {
            // StationIdEveryNUnits=4: fires when unitCount > 0 && unitCount % 4 == 0 (STORY-136,
            // SPEC F42.1). Across units 0-4, only unit 4 (unitCount==4) fires — never unit 0 (boot)
            // — so exactly one station-id appears in this 15-item window.
            var stationIds = produced
                .Where(i => i.MediaId.StartsWith("tts:stationid", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(stationIds);
        }
    }

    public sealed class ScenarioEveryProducedItemIsFullyFormedWithStampedId
    {
        readonly List<MediaItem> produced;

        public ScenarioEveryProducedItemIsFullyFormedWithStampedId()
        {
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = true,
                StationIdEveryNUnits = 4,
            };
            var catalog = new FakeMediaCatalog(MakeRef("track1"));
            var tts = new FakeTtsSegmentSource();

            // Unit 0 produces 3 items: StationId + LeadIn + Music (no BackAnnounce — no previous track)
            produced = ProduceN(BuildOrchestrator(catalog, tts, cadence), 3);
        }

        [Fact]
        public void EveryItemHasNonNullNonEmptyMediaId() =>
            Assert.All(produced, item => Assert.False(string.IsNullOrEmpty(item.MediaId)));

        [Fact]
        public void TtsItemsAreNamespacedWithTtsColonPrefix()
        {
            var ttsItems = produced.Where(i => i.MediaId.StartsWith("tts:", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(ttsItems);
        }

        [Fact]
        public void MusicItemsHaveNoTtsColonPrefix()
        {
            var music = produced.Where(i => !i.MediaId.StartsWith("tts:", StringComparison.Ordinal)).ToList();
            Assert.All(music, item => Assert.False(
                item.MediaId.StartsWith("tts:", StringComparison.Ordinal)));
        }
    }

    public sealed class ScenarioFiltersTtsIdsOutOfMusicExcludeList
    {
        readonly FakeMediaCatalog fakeCatalog;

        public ScenarioFiltersTtsIdsOutOfMusicExcludeList()
        {
            fakeCatalog = new FakeMediaCatalog(MakeRef("m3"));
            var orchestrator = BuildOrchestrator(fakeCatalog, new FakeTtsSegmentSource());
            var ctx = new PlayoutContext(["m1", "tts:abc", "m2"]);
            orchestrator.GetNextAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void CatalogReceivesExcludeListWithoutTtsIds() =>
            Assert.DoesNotContain("tts:abc", fakeCatalog.RotationCallOrderedRecentIds[0]);

        [Fact]
        public void TtsIdIsNeverPassedAsMusicExcludeId() =>
            Assert.All(
                fakeCatalog.RotationCallOrderedRecentIds[0],
                id => Assert.False(id.StartsWith("tts:", StringComparison.Ordinal)));
    }

    public sealed class ScenarioMusicSelectionUsesStationScope
    {
        readonly FakeMediaCatalog fakeCatalog;

        public ScenarioMusicSelectionUsesStationScope()
        {
            fakeCatalog = new FakeMediaCatalog(MakeRef("m1"));
            var scope = new LibraryScope([1L]);
            var orchestrator = BuildOrchestrator(fakeCatalog, new FakeTtsSegmentSource(), scope: scope);
            var ctx = new PlayoutContext([]);
            orchestrator.GetNextAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void CatalogIsCalledWithStationContextScope() =>
            Assert.Equal(new long[] { 1L }, fakeCatalog.RotationCallScopes[0].LibraryIds.ToArray());
    }

    public sealed class ScenarioCadenceConfigDefaultsAreExplicit
    {
        [Fact]
        public void LeadInBeforeEachTrackDefaultIsTrue() =>
            Assert.True(new CadenceConfig().LeadInBeforeEachTrack);

        [Fact]
        public void BackAnnounceAfterEachTrackDefaultIsTrue() =>
            Assert.True(new CadenceConfig().BackAnnounceAfterEachTrack);

        [Fact]
        public void StationIdEveryNUnitsDefaultIsFour() =>
            Assert.Equal(4, new CadenceConfig().StationIdEveryNUnits);
    }

    public sealed class ScenarioCadenceAppliesLiveBetweenUnits
    {
        // Regression pin for gitea-#211: Station:Cadence:* is advertised Live in the settings allowlist,
        // but Orchestrator used to read station.Cadence off a boot-frozen StationContext singleton
        // — a live PUT /api/settings edit persisted but never applied until an api restart. This
        // proves cadence is read fresh PER UNIT through ICadenceProvider: flipping the fake
        // provider's cadence between two GetNextAsync units — no re-construction — changes the
        // very next unit's plan (LeadIn off → on).
        [Fact]
        public async Task SecondUnitHonorsACadenceChangeMadeAfterTheFirst()
        {
            var catalog = new FakeMediaCatalog(MakeRef("m1"));
            var tts = new FakeTtsSegmentSource();
            var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 0,
            });
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts,
                new FakeActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
                new SpeechDeferralQueue(TimeProvider.System),
                TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
            var ctx = new PlayoutContext([]);

            // Unit 1 — cadence has LeadIn off: the pulled item is music, no lead-in precedes it.
            var firstItem = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(firstItem);
            Assert.False(
                firstItem.MediaId.StartsWith("tts:", StringComparison.Ordinal),
                $"Expected no lead-in with LeadIn off, got: {firstItem.MediaId}");

            // The live edit: no re-construction, no restart — same provider instance, new value.
            cadenceProvider.Cadence = cadenceProvider.Cadence with { LeadInBeforeEachTrack = true };

            // Unit 2 — cadence now has LeadIn on: the very next unit leads with a lead-in segment.
            var secondItem = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(secondItem);
            Assert.True(
                secondItem.MediaId.StartsWith("tts:leadin", StringComparison.OrdinalIgnoreCase),
                $"Expected a lead-in after the live cadence flip, got: {secondItem.MediaId}");
        }
    }

    // -------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------

    public sealed class ScenarioEmptyCatalogYieldsNullNonFatal
    {
        readonly Orchestrator orchestrator;

        public ScenarioEmptyCatalogYieldsNullNonFatal()
        {
            // Passing null makes GetRandomReadyAsync always return null
            var fakeCatalog = new FakeMediaCatalog(null);
            orchestrator = BuildOrchestrator(fakeCatalog, new FakeTtsSegmentSource());
        }

        [Fact]
        public async Task GetNextAsyncReturnsNullWhenCatalogReturnsNull() =>
            Assert.Null(await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None));

        [Fact]
        public async Task NoExceptionIsThrown()
        {
            var ex = await Record.ExceptionAsync(
                () => orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None));
            Assert.Null(ex);
        }
    }

    public sealed class ScenarioMusicItemCarriesCatalogCuePoints
    {
        // Regression for the T026 live-verification finding: the Orchestrator maps
        // MediaReference → MediaItem by hand, and silently dropped Cue — so the feeder never
        // stamped liq_cue_in/liq_cue_out even though the catalog row carried cue points.
        [Fact]
        public async Task CueFlowsFromMediaReferenceToMediaItem()
        {
            var cued = MakeRef("m1") with { Cue = new CuePoints(3.0, 27.0) };
            var orchestrator = BuildOrchestrator(
                new FakeMediaCatalog(cued),
                new FakeTtsSegmentSource(),
                new CadenceConfig
                {
                    LeadInBeforeEachTrack = false,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                });

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal(new CuePoints(3.0, 27.0), item.Cue);
        }
    }

    public sealed class ScenarioMusicItemCarriesCatalogEnergyAnnotations
    {
        // Regression for E6: the Orchestrator maps MediaReference → MediaItem by hand and dropped
        // IntroEnergy/OutroEnergy, making the LiquidsoapControl energy-stamp dead code on air.

        static Orchestrator BuildMinimalOrchestrator(MediaReference reference) =>
            BuildOrchestrator(
                new FakeMediaCatalog(reference),
                new FakeTtsSegmentSource(),
                new CadenceConfig
                {
                    LeadInBeforeEachTrack = false,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                });

        [Fact]
        public async Task IntroEnergyFlowsFromMediaReferenceToMediaItem()
        {
            var reference = MakeRef("m1") with { IntroEnergy = 0.82 };
            var orchestrator = BuildMinimalOrchestrator(reference);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal(0.82, item.IntroEnergy);
        }

        [Fact]
        public async Task OutroEnergyFlowsFromMediaReferenceToMediaItem()
        {
            var reference = MakeRef("m1") with { OutroEnergy = 0.45 };
            var orchestrator = BuildMinimalOrchestrator(reference);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal(0.45, item.OutroEnergy);
        }
    }

    public sealed class ScenarioMusicItemCarriesCatalogTags
    {
        // Regression for STORY-117 (T1 review, SPEC F34.7): proves the shared
        // MediaReferenceExtensions.ToMediaItem() narrowing Orchestrator uses carries
        // Album/Genre/Year through — the same class of "hand-mapped and dropped a field" bug
        // the Cue and Energy regressions above already guard against.

        static Orchestrator BuildMinimalOrchestrator(MediaReference reference) =>
            BuildOrchestrator(
                new FakeMediaCatalog(reference),
                new FakeTtsSegmentSource(),
                new CadenceConfig
                {
                    LeadInBeforeEachTrack = false,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                });

        [Fact]
        public async Task AlbumFlowsFromMediaReferenceToMediaItem()
        {
            var reference = MakeRef("m1") with { Album = "Boy" };
            var orchestrator = BuildMinimalOrchestrator(reference);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("Boy", item.Album);
        }

        [Fact]
        public async Task GenreFlowsFromMediaReferenceToMediaItem()
        {
            var reference = MakeRef("m1") with { Genre = "Indie Rock" };
            var orchestrator = BuildMinimalOrchestrator(reference);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("Indie Rock", item.Genre);
        }

        [Fact]
        public async Task YearFlowsFromMediaReferenceToMediaItem()
        {
            var reference = MakeRef("m1") with { Year = 1987 };
            var orchestrator = BuildMinimalOrchestrator(reference);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal(1987, item.Year);
        }
    }

    public sealed class ScenarioOrchestrationProjectDependsOnlyOnCore
    {
        [Fact]
        public void OrchestrationAssemblyReferencesOnlyCore()
        {
            var deps = typeof(Orchestrator).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty);
            Assert.DoesNotContain("GenWave.MediaLibrary", deps);
        }

        [Fact]
        public void OrchestrationAssemblyDoesNotReferenceTts()
        {
            var deps = typeof(Orchestrator).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty);
            Assert.DoesNotContain("GenWave.Tts", deps);
        }
    }
}
