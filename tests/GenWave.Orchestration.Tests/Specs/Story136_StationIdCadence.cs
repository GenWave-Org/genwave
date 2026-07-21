// STORY-136 — Station IDs: no boot blast, a real off switch (Epic V / SPEC F42, closes gitea-#216) —
// orchestrator half. The validation half lives in
// Host.Tests/Specs/Story136_StationIdCadenceValidation.cs.
//
// BDD specification — xUnit. Implemented V6 (2026-07-14): the guard becomes
// unitCount > 0 && unitCount % N == 0 — with N=4 the FIRST station ID airs at unit 4 (the unit
// whose 0-indexed position equals N, i.e. once N units have elapsed), never at boot.
//
// LeadIn/BackAnnounce are off throughout so each unit's plan is at most [StationId, Music] — one
// music track per unit, letting "which unit position aired an ID" be read straight off the
// produced sequence's music-track boundaries rather than untangling lead-in/back-announce noise.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStationIdCadence
{
    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Orchestrator BuildOrchestrator(int stationIdEveryNUnits)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = stationIdEveryNUnits,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeRef("track1"));
        var tts = new FakeTtsSegmentSource();
        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts,
            new FakeActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
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

    static bool IsStationId(MediaItem item) =>
        item.MediaId.StartsWith("tts:stationid", StringComparison.OrdinalIgnoreCase);

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    /// <summary>
    /// Indices (into <paramref name="produced"/>) of every music track, in unit order — with
    /// LeadIn/BackAnnounce off, each unit contributes exactly one music track, so
    /// <c>musicIndices[p]</c> is unit position <c>p</c>'s track.
    /// </summary>
    static List<int> MusicIndices(List<MediaItem> produced) =>
        produced
            .Select((item, idx) => (item, idx))
            .Where(x => IsMusic(x.item))
            .Select(x => x.idx)
            .ToList();

    /// <summary>True when the item immediately before <paramref name="musicIndex"/> is a station ID.</summary>
    static bool PrecededByStationId(List<MediaItem> produced, int musicIndex) =>
        musicIndex > 0 && IsStationId(produced[musicIndex - 1]);

    // -------------------------------------------------------------------------
    // HAPPY PATH
    // -------------------------------------------------------------------------

    public sealed class ScenarioUnitZeroNeverAirsAStationId
    {
        // N=4: unit positions 0-8 need at most 11 raw items (7 silent units + 2 firing units at
        // p=4 and p=8, each contributing 2 items) — 16 is comfortable headroom.
        readonly List<MediaItem> produced;
        readonly List<int> musicIndices;

        public ScenarioUnitZeroNeverAirsAStationId()
        {
            var orchestrator = BuildOrchestrator(stationIdEveryNUnits: 4);
            produced = ProduceN(orchestrator, 16);
            musicIndices = MusicIndices(produced);
        }

        [Fact]
        public void TheFirstUnitAfterBootAirsNoStationId()
        {
            // Unit position 0 (the very first produced item, since LeadIn/BackAnnounce are off):
            // no station ID precedes it — there IS no item before index 0.
            Assert.True(IsMusic(produced[0]));
        }

        [Fact]
        public void TheFirstStationIdAirsAtUnitN()
        {
            // Unit positions 0-3 (musicIndices[0..3]) never carry a preceding station ID; unit
            // position 4 (musicIndices[4]) — once N=4 units have elapsed — does.
            for (int p = 0; p < 4; p++)
                Assert.False(PrecededByStationId(produced, musicIndices[p]),
                    $"Unit position {p} must NOT be preceded by a station ID (F42.1).");

            Assert.True(PrecededByStationId(produced, musicIndices[4]),
                "Unit position 4 (unitCount==N==4) must be preceded by the first station ID (F42.1).");
        }

        [Fact]
        public void SubsequentStationIdsAirEveryNUnitsThereafter()
        {
            // Unit positions 5-7 go quiet again; unit position 8 (unitCount==8, the next multiple
            // of N=4) carries the second station ID.
            for (int p = 5; p < 8; p++)
                Assert.False(PrecededByStationId(produced, musicIndices[p]),
                    $"Unit position {p} must NOT be preceded by a station ID (F42.1).");

            Assert.True(PrecededByStationId(produced, musicIndices[8]),
                "Unit position 8 (the next multiple of N=4) must be preceded by a station ID (F42.1).");
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioZeroDisablesStationIds
    {
        [Fact]
        public void NoStationIdEverAirsAtDepthZero()
        {
            var orchestrator = BuildOrchestrator(stationIdEveryNUnits: 0);
            var produced = ProduceN(orchestrator, 20);

            Assert.DoesNotContain(produced, IsStationId);
        }
    }
}
