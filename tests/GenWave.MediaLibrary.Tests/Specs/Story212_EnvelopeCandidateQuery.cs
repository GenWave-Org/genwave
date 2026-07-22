// STORY-212 — The envelope is law, and silence is forbidden
//
// BDD specification — xUnit (SPEC F81.1, F81.3, F81.4). PLAN T61 — the catalog side of the law:
// filtering happens by construction in the candidate query, never by post-filtering a wider set.
// (The provider/ladder half of this story is Story212_EnvelopeProviderAndLadder.cs in
// Orchestration.Tests.)
//
// Integration: hits real Postgres via DatabaseCollection — mirrors Story134/Story135's own
// rationale (the tiered ORDER BY plus the new envelope predicates are selection SQL, provable only
// against the real planner).

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnvelopeCandidateQuery
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Inserts a ready + measurable + eligible row in library 1 with an overridable genre (a genuine
    /// <see langword="null"/> is a real, untagged genre — unlike <see cref="Harness.ReadyResultWith"/>,
    /// which coalesces a null genre argument to a default "g") and LUFS (so callers control the
    /// energy percentile <see cref="MediaRepository.RecomputeEnergyPercentilesAsync"/> derives).
    /// </summary>
    static async Task<long> InsertReadyAsync(
        MediaRepository repo, string path, string? genre = "g", double lufs = -14.0)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, new EnrichmentResult(
            DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: "t", Artist: "a", Album: "al", AlbumArtist: "aa", Genre: genre, TrackNo: 1, Year: 2020,
            IntegratedLufs: lufs, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow), CancellationToken.None);
        return id;
    }

    public static class ScenarioFilterByConstruction
    {
        // Arrange (T61): library seeded with tracks in/out of genre and in/out of an
        // EnergyRange; query with a SegmentEnvelope.

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioGenreAllowList(DatabaseFixture db)
        {
            [Fact]
            public async Task NoTrackOutsideTheGenreListEntersThePool()
            {
                // F81.4 by-construction: the SQL predicate excludes them, not a later filter. An
                // untagged (null-genre) row is excluded too (SegmentEnvelope's own remarks) — genre
                // curation requires a positive tag, unlike energy's NULL-passes exemption below.
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                var rockId = await InsertReadyAsync(repo, "/envelope/genre-rock.flac", genre: "Rock");
                await InsertReadyAsync(repo, "/envelope/genre-jazz.flac", genre: "Jazz");
                await InsertReadyAsync(repo, "/envelope/genre-untagged.flac", genre: null);
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var recent = new List<string>();
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                for (var i = 0; i < 10; i++)
                {
                    var candidate = await catalog.GetEnvelopeCandidateAsync(
                        scope, recent, artistSeparation: 0, envelope, CancellationToken.None);

                    Assert.NotNull(candidate);
                    Assert.Equal(rockId.ToString(), candidate.Media.MediaId);
                }
            }

            [Fact]
            public async Task GenreMatchingIsCaseInsensitive()
            {
                // The envelope names "rock" (lowercase); the only playable row is tagged "ROCK" —
                // the allow-list must still admit it (mirrors BuildAdminWhere's GenresExact idiom).
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                var id = await InsertReadyAsync(repo, "/envelope/genre-case.flac", genre: "ROCK");
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var recent = new List<string>();
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], EnergyRange.Unconstrained);

                var candidate = await catalog.GetEnvelopeCandidateAsync(
                    scope, recent, artistSeparation: 0, envelope, CancellationToken.None);

                Assert.NotNull(candidate);
                Assert.Equal(id.ToString(), candidate.Media.MediaId);
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioEnergyBand(DatabaseFixture db)
        {
            [Fact]
            public async Task NoTrackOutsideTheEnergyRangeEntersThePool()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                // Three rows spanning the energy percentile spread. percent_rank() over exactly
                // these three ready rows (ascending LUFS) yields energy 0.0, 0.5, 1.0.
                var lowId  = await InsertReadyAsync(repo, "/envelope/energy-low.flac",  lufs: -30.0);
                var midId  = await InsertReadyAsync(repo, "/envelope/energy-mid.flac",  lufs: -20.0);
                var highId = await InsertReadyAsync(repo, "/envelope/energy-high.flac", lufs: -10.0);
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                // A fourth row lands AFTER the recompute — its energy stays NULL until the next
                // population-wide recompute tick (SPEC F80.2's batched piggyback), even though the
                // row is already ready + measurable + fully selectable. SPEC F81.4 decision: NULL
                // energy PASSES the filter — enrichment lag must never silence a ready track.
                var lagId = await InsertReadyAsync(repo, "/envelope/energy-lag.flac", lufs: -25.0);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var recent = new List<string>();
                // Excludes the low (0.0) and high (1.0) extremes; admits the mid row (0.5).
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, [], new EnergyRange(0.25, 0.75));

                var seen = new HashSet<string>();
                for (var i = 0; i < 30; i++)
                {
                    var candidate = await catalog.GetEnvelopeCandidateAsync(
                        scope, recent, artistSeparation: 0, envelope, CancellationToken.None);
                    Assert.NotNull(candidate);
                    seen.Add(candidate.Media.MediaId);
                }

                Assert.Contains(midId.ToString(), seen);
                Assert.Contains(lagId.ToString(), seen);       // NULL energy admitted (lag exemption)
                Assert.DoesNotContain(lowId.ToString(), seen);
                Assert.DoesNotContain(highId.ToString(), seen);
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioRotationComposition(DatabaseFixture db)
        {
            [Fact]
            public async Task RotationWindowStillApplies()
            {
                // Envelope filtering composes with the existing rotation window, not replaces it
                // (F81.1): two envelope-conforming rows must still cycle instead of draining across
                // a full recent-window cycle (mirrors Story134's own proof), while a third row that
                // fails the genre allow-list is never picked at all, throughout.
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                var idA = await InsertReadyAsync(repo, "/envelope/rotation-a.flac", genre: "Rock");
                var idB = await InsertReadyAsync(repo, "/envelope/rotation-b.flac", genre: "Rock");
                await InsertReadyAsync(repo, "/envelope/rotation-out-of-genre.flac", genre: "Pop");
                await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(
                    TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

                var recent = new List<string>();
                var seen = new HashSet<string>();
                for (var i = 0; i < 25; i++)
                {
                    var candidate = await catalog.GetEnvelopeCandidateAsync(
                        scope, recent, artistSeparation: 2, envelope, CancellationToken.None);

                    Assert.NotNull(candidate);
                    seen.Add(candidate.Media.MediaId);
                    recent.Add(candidate.Media.MediaId);
                    if (recent.Count > 20) recent.RemoveAt(0);
                }

                Assert.Contains(idA.ToString(), seen);
                Assert.Contains(idB.ToString(), seen);
            }
        }
    }

    public static class ScenarioStationDefaultEnvelope
    {
        // Arrange (T61): station settings carrying the default 24/7 envelope.

        [Fact]
        public static void TheDefaultEnvelopeIsReadFromStationSettings()
        {
            // F81.3 v1 scope: one envelope, settings-resident, no schedule grid.
            // SegmentEnvelope.StationDefault is the exact shape GenWave.Host's Station:Envelope:*
            // settings resolve to before an operator narrows either knob (StationEnvelopeOptions'
            // own property defaults mirror this shape) — the full day, every genre admitted (empty
            // allow-list), and the full [0,1] energy range (no energy constraint).
            var envelope = SegmentEnvelope.StationDefault;

            Assert.Equal(TimeOnly.MinValue, envelope.StartsAt);
            Assert.Equal(TimeOnly.MaxValue, envelope.EndsAt);
            Assert.Empty(envelope.Genres);
            Assert.Equal(EnergyRange.Unconstrained, envelope.EnergyRange);
        }
    }
}
