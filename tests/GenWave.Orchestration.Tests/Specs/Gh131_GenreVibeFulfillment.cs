// gh-#131 — "Anything metal!!" means metal, not Rock (genre predicate, fulfillment rung)
//
// BDD specification — xUnit. Owns the FULFILLMENT half of gh-#131 inside this project's seam: the
// REAL RequestFulfillmentProvider resolving a genre-carrying pending row through
// IRequestCatalogProbe.FindVibeAsync's widened (moods, genre, envelope) shape — proving a genre-only
// request rides the same vibe machinery a moods-only request always did, and that genre+mood arrive
// TOGETHER at the probe (they AND inside its WHERE clause — predicates merge, never compete). The
// probe SQL itself is MediaLibrary.Tests' Gh131_GenreRequestCatalog.cs; the parse/match/intake side
// is Host.Tests' Gh131_GenreRequestPredicates.cs — same three-file split STORY-226/227 established.

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureGenreVibeFulfillment
{
    // ---------------------------------------------------------------------
    // Helpers — mirrors Story227_RequestFulfillment's FeatureRequestFulfillmentProvider shapes
    // ---------------------------------------------------------------------

    static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    static readonly SegmentEnvelope Envelope =
        new(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], new EnergyRange(0.2, 0.8));

    static MediaReference MakeRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static RequestFulfillmentProvider BuildProvider(FakeRequestStore store, FakeRequestCatalogProbe probe) =>
        new(store, probe, new FakeRequestOverrideEnvelopeProvider(true),
            NoOpStationEventSink.Instance, new FakeTimeProvider(Now));

    // ---------------------------------------------------------------------
    // HAPPY PATH — a genre-only request resolves through the vibe machinery
    // ---------------------------------------------------------------------

    public static class ScenarioGenreOnlyRequest
    {
        [Fact]
        public static async Task AGenreOnlyRequestConstrainsThePickThroughFindVibe()
        {
            // Arrange: a genre-only (no match, no moods) pending request; the probe only returns a
            // hit when called with exactly that genre predicate and NO moods — a moods-shaped call,
            // or a dropped genre, would miss.
            var genreMedia = MakeRef("metal1");
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), genre: "Metal");
            var probe = new FakeRequestCatalogProbe
            {
                OnFindVibe = (moods, genre, _) => moods.Count == 0 && genre == "Metal" ? genreMedia : null,
            };
            var provider = BuildProvider(store, probe);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the genre-matched candidate came back, flagged as a vibe fulfillment.
            Assert.Equal(genreMedia.MediaId, result?.Candidate.Media.MediaId);
            Assert.True(result?.WasVibe);
        }

        [Fact]
        public static async Task AFulfilledGenreRequestStampsTheOneShotCas()
        {
            // Arrange: same genre-only row, always-hitting probe.
            var store = new FakeRequestStore();
            var id = store.AddPending(Now.AddMinutes(10), genre: "Metal");
            var probe = new FakeRequestCatalogProbe { OnFindVibe = (_, _, _) => MakeRef("metal1") };
            var provider = BuildProvider(store, probe);

            // Act.
            await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the row is consumed exactly like a matched/mood fulfillment — one-shot (F87.6).
            Assert.Equal("fulfilled", store.StatusOf(id));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — genre and mood predicates arrive together (AND merge)
    // ---------------------------------------------------------------------

    public static class ScenarioGenrePlusMoodRequest
    {
        [Fact]
        public static async Task GenreAndMoodBothReachTheProbeInOneCall()
        {
            // Arrange: a genre+mood pending request; the probe demands BOTH predicates in the same
            // call — proving neither is dropped nor resolved in a separate pass.
            var bothMedia = MakeRef("dreamy-metal");
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), moods: ["dreamy"], genre: "Metal");
            var probe = new FakeRequestCatalogProbe
            {
                OnFindVibe = (moods, genre, _) =>
                    moods.Contains("dreamy") && genre == "Metal" ? bothMedia : null,
            };
            var provider = BuildProvider(store, probe);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the AND-satisfying candidate came back.
            Assert.Equal(bothMedia.MediaId, result?.Candidate.Media.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unsatisfiable genre idles the row, exactly like any vibe miss
    // ---------------------------------------------------------------------

    public static class ScenarioGenreVibeMiss
    {
        [Fact]
        public static async Task AGenreTheProbeCannotSatisfyLeavesTheRowPending()
        {
            // Arrange: a genre-only row and an always-missing probe (e.g. the last stocked row of
            // that genre was vetoed after parse time).
            var store = new FakeRequestStore();
            var id = store.AddPending(Now.AddMinutes(10), genre: "Metal");
            var probe = new FakeRequestCatalogProbe { OnFindVibe = (_, _, _) => null };
            var provider = BuildProvider(store, probe);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: no fulfillment, and the row idles toward its own expiry untouched (F87.6).
            Assert.Null(result);
            Assert.Equal("pending", store.StatusOf(id));
        }
    }
}
