// STORY-250 — A station that knows its audience (gh-#174, SPEC F95.4, PLAN T114)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration). The pool predicate is enforced
// by construction in the catalog candidate query — these facts drive the query seam against a real
// Postgres (Story-catalog idiom, mirroring Story212_EnvelopeCandidateQuery.cs and Story226's own
// gh-#99 exclusion facts). The setting surface + F95.6 end-to-end pins live in
// Story250_AudiencePostureSetting.cs (Host.Tests).
//
// Three selection paths share the ONE predicate (SPEC F95.4): rotation (GetRotationCandidateAsync),
// boundary bias (GetEnvelopeCandidatePoolAsync — the SAME query the Orchestrator's boundary-bias
// resample loop draws from, never a separate statement), and the request matcher
// (IRequestCatalogProbe.FindBestAsync). Mature plays everything, unmasked; an unclassified
// (explicit IS NULL) row plays under either posture — unknown-is-explicit was declined at /explore.

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureExplicitPoolExclusion
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Inserts a ready + measurable + eligible row in library 1, classified <paramref name="explicitFlag"/>
    /// via the SAME tag-pass write path (<see cref="MediaRepository.WriteEnrichmentAsync"/>) T112 uses —
    /// a non-null <paramref name="explicitFlag"/> stamps <c>explicit_source = 'tag'</c>, exactly how a
    /// real advisory-tag scan classifies the gh-#174 fixture. <see langword="null"/> leaves the row
    /// unclassified (SPEC F95.2's "NULL = unknown", never a sentinel false).
    /// </summary>
    static async Task<long> InsertReadyAsync(
        MediaRepository repo, string path, bool? explicitFlag, string artist = "a", string title = "t")
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, new EnrichmentResult(
            DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: title, Artist: artist, Album: "al", AlbumArtist: "aa", Genre: "g", TrackNo: 1, Year: 2020,
            Explicit: explicitFlag,
            IntegratedLufs: -14.0, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow), CancellationToken.None);
        return id;
    }

    static RequestCatalogProbeRepository Probe(DatabaseFixture db, AudiencePosture posture) =>
        new(db.DataSource, new FakeSafeScopeProvider(), new FakeAudiencePostureProvider(posture),
            NullLogger<RequestCatalogProbeRepository>.Instance);

    public static class ScenarioEveryoneExcludesAtThePool
    {
        // Given a track classified explicit and posture everyone (F95.4).

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioRotation(DatabaseFixture db)
        {
            [Fact]
            public async Task RotationCandidateQueryNeverReturnsIt()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));

                var explicitId = await InsertReadyAsync(repo, "/explicit/rotation-explicit.flac", explicitFlag: true);
                var admittedId = await InsertReadyAsync(repo, "/explicit/rotation-admitted.flac", explicitFlag: false);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);

                for (var i = 0; i < 15; i++)
                {
                    var candidate = await catalog.GetRotationCandidateAsync(scope, [], artistSeparation: 0, CancellationToken.None);

                    Assert.NotNull(candidate);
                    Assert.Equal(admittedId.ToString(), candidate.Media.MediaId);
                    Assert.NotEqual(explicitId.ToString(), candidate.Media.MediaId);
                }
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioRequestMatcher(DatabaseFixture db)
        {
            [Fact]
            public async Task RequestMatcherNeverMatchesIt()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db);
                await InsertReadyAsync(
                    repo, "/explicit/matcher-explicit.flac", explicitFlag: true,
                    artist: "Explicit Artist", title: "Explicit Title");

                var found = await Probe(db, AudiencePosture.Everyone)
                    .FindBestAsync("Explicit Artist", null, null, CancellationToken.None);

                Assert.Null(found);
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioBoundaryBias(DatabaseFixture db)
        {
            [Fact]
            public async Task BoundaryBiasSamplingNeverSeesIt()
            {
                // Boundary bias resamples GetEnvelopeCandidatePoolAsync (T43) — the SAME query, never a
                // separate statement — so proving the exclusion here proves it for boundary bias too.
                await db.ResetAsync();
                var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));

                var explicitId = await InsertReadyAsync(repo, "/explicit/boundary-explicit.flac", explicitFlag: true);
                var admittedId = await InsertReadyAsync(repo, "/explicit/boundary-admitted.flac", explicitFlag: false);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);
                var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);

                var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                    scope, [], artistSeparation: 0, envelope, limit: 20, CancellationToken.None);

                Assert.Contains(pool, c => c.Media.MediaId == admittedId.ToString());
                Assert.DoesNotContain(pool, c => c.Media.MediaId == explicitId.ToString());
            }
        }

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioFulfillmentRecheck(DatabaseFixture db)
        {
            [Fact]
            public async Task GetSelectableByIdNeverReturnsIt()
            {
                // The rung -1 air path: a request matched earlier, but an operator/sweep stamps the row
                // explicit before fulfillment re-checks it via LawAndSafeScopeWhereParts (F95.4 parity
                // with FindBestAsync's own match-time check).
                await db.ResetAsync();
                var repo = Harness.Repo(db);

                var explicitId = await InsertReadyAsync(
                    repo, "/explicit/recheck-explicit.flac", explicitFlag: true,
                    artist: "Recheck Artist", title: "Recheck Title");

                var found = await Probe(db, AudiencePosture.Everyone)
                    .GetSelectableByIdAsync(explicitId, envelope: null, CancellationToken.None);

                Assert.Null(found);
            }
        }
    }

    public static class ScenarioMaturePlaysEverything
    {
        // Given posture mature (F95.4).

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioRotation(DatabaseFixture db)
        {
            [Fact]
            public async Task TheSameTrackIsEligibleUnmasked()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Mature));

                var explicitId = await InsertReadyAsync(repo, "/explicit/mature-explicit.flac", explicitFlag: true);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);

                var candidate = await catalog.GetRotationCandidateAsync(scope, [], artistSeparation: 0, CancellationToken.None);

                Assert.NotNull(candidate);
                Assert.Equal(explicitId.ToString(), candidate.Media.MediaId);
            }
        }
    }

    public static class ScenarioUnknownPlays
    {
        // Given explicit = NULL on posture everyone (unknown-is-explicit was declined).

        [Collection(DatabaseCollection.Name)]
        [Trait("Category", "Integration")]
        public sealed class ScenarioRotation(DatabaseFixture db)
        {
            [Fact]
            public async Task UnclassifiedTracksRemainInThePool()
            {
                await db.ResetAsync();
                var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));

                var unclassifiedId = await InsertReadyAsync(repo, "/explicit/unclassified.flac", explicitFlag: null);

                var catalog = (IMediaCatalog)repo;
                var scope = new LibraryScope([1L]);

                var candidate = await catalog.GetRotationCandidateAsync(scope, [], artistSeparation: 0, CancellationToken.None);

                Assert.NotNull(candidate);
                Assert.Equal(unclassifiedId.ToString(), candidate.Media.MediaId);
            }
        }
    }
}
