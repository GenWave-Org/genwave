// gh-#259 — DJ attribution is stamped on the item at PLAN time
//
// After a schedule boundary the engine queue can still hold the previous show's rendered items;
// the spectator dj field follows the AIRING item, so every item the Orchestrator plans must carry
// the show persona it was planned under. Voice/credit rules (F35.3, gh-#96) are untouched — DjName
// is a separate attribution stamp, never a voice or an Artist credit.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAttributionFromTheAiringItem
{
    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Persona BuildPersona(string name) =>
        new(1, name, "", "", "am_onyx", DateTime.UtcNow, DateTime.UtcNow);

    static (Orchestrator Orchestrator, FakeTtsSegmentSource Tts) BuildOrchestrator(
        FakeActivePersonaAccessor accessor, int stationIdEveryNUnits = 0)
    {
        var cadence = new CadenceConfig
        {
            LeadInBeforeEachTrack = true,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = stationIdEveryNUnits,
        };
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "af_heart"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeRef("track1"));
        var tts = new FakeTtsSegmentSource();
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts, accessor,
            NullLogger<Orchestrator>.Instance, new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
        return (orchestrator, tts);
    }

    /// <summary>Pulls until the provider has been drained of <paramref name="pulls"/> items,
    /// collecting every returned <see cref="MediaItem"/> in air order.</summary>
    static async Task<List<MediaItem>> PullAsync(Orchestrator orchestrator, int pulls)
    {
        var items = new List<MediaItem>();
        var ctx = new PlayoutContext([]);
        for (var i = 0; i < pulls; i++)
        {
            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(item);
            items.Add(item);
        }
        return items;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioMusicIsStampedWithTheShowPersona
    {
        [Fact]
        public async Task TheMusicItemCarriesTheActivePersonasName()
        {
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("DJ Nova") };
            var (orchestrator, _) = BuildOrchestrator(accessor);

            var items = await PullAsync(orchestrator, 2); // [LeadIn, Music]

            var music = Assert.Single(items, i => i.MediaId == "track1");
            Assert.Equal("DJ Nova", music.DjName);
        }

        [Fact]
        public async Task AStationIdSegmentIsStampedWithTheUnitPersonaButKeepsTheStationVoice()
        {
            // gh-#96's imaging carve-out stands (station voice, PersonaName null on the request) —
            // but the ID still airs inside the show, so its attribution stamp is the unit persona:
            // the dj line must not flicker to "no DJ" for a few seconds of imaging mid-show.
            var accessor = new FakeActivePersonaAccessor { Persona = BuildPersona("DJ Nova") };
            var (orchestrator, tts) = BuildOrchestrator(accessor, stationIdEveryNUnits: 1);

            // Unit 1 = [LeadIn, Music]; unit 2 = [StationId, LeadIn, Music] — five pulls total.
            var items = await PullAsync(orchestrator, 5);

            var stationId = Assert.Single(items, i => i.MediaId.StartsWith("tts:stationid", StringComparison.Ordinal));
            Assert.Equal("DJ Nova", stationId.DjName);
            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Equal("af_heart", request.Voice);   // still station imaging
            Assert.Null(request.PersonaName);          // still the station's credit
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioNoPersonaMeansNoAttribution
    {
        [Fact]
        public async Task WithNoActivePersonaTheMusicItemCarriesNullDjName()
        {
            var accessor = new FakeActivePersonaAccessor(); // Persona stays null
            var (orchestrator, _) = BuildOrchestrator(accessor);

            var items = await PullAsync(orchestrator, 2);

            var music = Assert.Single(items, i => i.MediaId == "track1");
            Assert.Null(music.DjName);
        }

        [Fact]
        public async Task AnAccessorFaultDegradesToNoAttributionAndNeverCostsTheSlot()
        {
            // F12.4 posture, same as every other accessor consumer: the pick must still succeed.
            var accessor = new FakeActivePersonaAccessor
            {
                ThrowOnResolve = new InvalidOperationException("persona store unreachable"),
            };
            var (orchestrator, _) = BuildOrchestrator(accessor);

            var items = await PullAsync(orchestrator, 2);

            var music = Assert.Single(items, i => i.MediaId == "track1");
            Assert.Null(music.DjName);
        }
    }
}
