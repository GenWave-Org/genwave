// STORY-226 — The station checks what it actually has (SPEC F87.5, PLAN T89)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration). Owns the CATALOG PROBE half of
// STORY-226: IRequestCatalogProbe.FindBestAsync against the real library schema (ready + measurable +
// eligible + not-never-play, the exact GetRandomReadyAsync/RandomSelectionProvider selectability
// predicate, PLUS the gh-#99 safe-scope exclusion — applied to a request's parsed artist/title, per
// Story111's own never-play idioms). The RequestMatcher
// DECISION TREE built on top of this probe — the vibe-fallback and unmatched-silently branches — is
// GenWave.Host.Requests.RequestMatcher, a Host-layer type this project cannot reference (no
// ProjectReference to GenWave.Host); those facts live in Host.Tests' own
// Story226_RequestMatcherDecisions.cs against a fake probe/store instead. Together the two files carry
// the same five facts this file was originally authored pending, split along the seam the project
// references actually allow.
//
// Also covers the two T90 additions to the SAME probe (SPEC F87.6, STORY-227): GetSelectableByIdAsync
// (the fulfillment rung's law-and-optionally-envelope re-check for a T89 match) and FindVibeAsync (the
// mood-machinery resolution for a vibe request) — same file, same repository, same probe concern.

using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRequestMatching
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RequestCatalogProbeRepository Probe(DatabaseFixture db) =>
        new(db.DataSource, new FakeSafeScopeProvider(), NullLogger<RequestCatalogProbeRepository>.Instance);

    static MediaRatingRepository RatingRepo(DatabaseFixture db) => new(db.DataSource, new FakeSafeScopeProvider());

    /// <summary>Inserts a ready + measurable + eligible row carrying the given artist/title — a row
    /// the probe's WHERE clause admits before any artist/title predicate is even applied.</summary>
    static async Task<long> InsertSelectableTrackAsync(DatabaseFixture db, string path, string artist, string title)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: title, artist: artist), CancellationToken.None);
        return id;
    }

    /// <summary>Inserts a ready + eligible row like <see cref="InsertSelectableTrackAsync"/>, but
    /// NOT measurable — the F87.5 selectability-predicate parity fact: a matched-but-unplayable row
    /// must not idle a request to expiry.</summary>
    static async Task<long> InsertUnmeasurableTrackAsync(DatabaseFixture db, string path, string artist, string title)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(
            id, Harness.ReadyResultWith(title: title, artist: artist) with { Measurable = false },
            CancellationToken.None);
        return id;
    }

    /// <summary>Creates a fresh named library — <paramref name="tag"/> keeps the name unique across
    /// facts, since <see cref="DatabaseFixture.ResetAsync"/> truncates <c>library.media</c> only, never
    /// <c>library.library</c> (mirrors <c>Gh099_SafeContentRatingRepository</c>'s own seed helper).</summary>
    static async Task<long> CreateLibraryAsync(DatabaseFixture db, string tag)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values (@name) returning id", new { name = $"safe-{tag}" });
    }

    /// <summary>Inserts a selectable row like <see cref="InsertSelectableTrackAsync"/>, then moves it
    /// into <paramref name="libraryId"/> — used to seed a row inside the gh-#99 safe scope.</summary>
    static async Task<long> InsertSelectableTrackInLibraryAsync(
        DatabaseFixture db, long libraryId, string path, string artist, string title)
    {
        var id = await InsertSelectableTrackAsync(db, path, artist, title);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set library_id = @libraryId where id = @id", new { libraryId, id });
        return id;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — an artist predicate finds the matching row
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioArtistPredicateMatch(DatabaseFixture db)
    {
        [Fact]
        public async Task AHeldArtistPredicateFindsTheMatchedMediaId()
        {
            // Arrange: a ready+eligible row by the requested artist, plus an unrelated distractor row.
            await db.ResetAsync();
            var wantedId = await InsertSelectableTrackAsync(db, "/req/match-artist-wanted.flac", "Led Zeppelin", "Kashmir");
            await InsertSelectableTrackAsync(db, "/req/match-artist-other.flac", "Some Other Band", "Some Other Song");

            // Act: probe for the artist alone (title null — a request may name only one).
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, CancellationToken.None);

            // Assert: exactly the matching row's id comes back (F87.5).
            Assert.Equal(wantedId, found);
        }

        [Fact]
        public async Task AnUnrecognizedArtistFindsNoMediaId()
        {
            // Arrange: a catalog with no row anywhere near the requested artist.
            await db.ResetAsync();
            await InsertSelectableTrackAsync(db, "/req/match-artist-nohit.flac", "Some Other Band", "Some Other Song");

            // Act: probe for an artist nothing in the catalog resembles.
            var found = await Probe(db).FindBestAsync("A Band That Does Not Exist", null, CancellationToken.None);

            // Assert: null — nothing to match (F87.5's "no match either way").
            Assert.Null(found);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — operator vetoes are law at match time (F87.5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVetoesExcludeAMatch(DatabaseFixture db)
    {
        [Fact]
        public async Task ANeverPlayOnlyMatchFindsNoMediaId()
        {
            // Arrange: the only row that would otherwise match is flagged never_play.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/match-never-play.flac", "Led Zeppelin", "Kashmir");
            await RatingRepo(db).SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            // Act: probe for that exact artist.
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, CancellationToken.None);

            // Assert: null — the flag suppresses the row from the probe entirely, same as main rotation.
            Assert.Null(found);
        }

        [Fact]
        public async Task AnIneligibleOnlyMatchFindsNoMediaId()
        {
            // Arrange: the only row that would otherwise match has been curated out (eligible=false).
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/match-ineligible.flac", "Led Zeppelin", "Kashmir");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set eligible = false where id = @id", new { id });

            // Act: probe for that exact artist.
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, CancellationToken.None);

            // Assert: null — an operator's curation exclusion is honored exactly like never_play.
            Assert.Null(found);
        }

        [Fact]
        public async Task AReadyButUnmeasurableOnlyMatchFindsNoMediaId()
        {
            // Arrange: the only row that would otherwise match is ready but never came back
            // measurable — the exact GetRandomReadyAsync/RandomSelectionProvider parity (F87.5).
            await db.ResetAsync();
            await InsertUnmeasurableTrackAsync(db, "/req/match-unmeasurable.flac", "Led Zeppelin", "Kashmir");

            // Act: probe for that exact artist.
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, CancellationToken.None);

            // Assert: null — an unmeasurable row isn't selectable; matching it would only idle the
            // request to expiry for nothing.
            Assert.Null(found);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — wildcard characters in the wish are literal, never patterns
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWildcardCharactersAreEscaped(DatabaseFixture db)
    {
        [Fact]
        public async Task APercentInTheArtistMatchesOnlyTheLiteralSubstring()
        {
            // Arrange: a row whose artist contains "100%" literally, and a decoy whose artist would
            // ONLY match if "%" were treated as an unescaped SQL wildcard.
            await db.ResetAsync();
            var literalId = await InsertSelectableTrackAsync(db, "/req/wildcard-percent-literal.flac", "100% Pure", "Track One");
            await InsertSelectableTrackAsync(db, "/req/wildcard-percent-decoy.flac", "100 Degrees", "Track Two");

            // Act: probe for the literal "100%" substring.
            var found = await Probe(db).FindBestAsync("100%", null, CancellationToken.None);

            // Assert: only the row actually containing "100%" comes back — "100%" never behaves as
            // "100" followed by anything.
            Assert.Equal(literalId, found);
        }

        [Fact]
        public async Task AnUnderscoreInTheArtistMatchesOnlyTheLiteralSubstring()
        {
            // Arrange: a row whose artist contains "a_b" literally, and a decoy that would ONLY match
            // if "_" were treated as an unescaped SQL single-character wildcard.
            await db.ResetAsync();
            var literalId = await InsertSelectableTrackAsync(db, "/req/wildcard-underscore-literal.flac", "a_b Collective", "Track One");
            await InsertSelectableTrackAsync(db, "/req/wildcard-underscore-decoy.flac", "aXb Collective", "Track Two");

            // Act: probe for the literal "a_b" substring.
            var found = await Probe(db).FindBestAsync("a_b", null, CancellationToken.None);

            // Assert: only the row actually containing "a_b" comes back — "_" never matches "X".
            Assert.Equal(literalId, found);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — gh-#99: a wish can never reach safe-scope content
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSafeScopeExcludesAMatch(DatabaseFixture db)
    {
        [Fact]
        public async Task ATitleMatchInsideTheLiveSafeScopeFindsNoMediaId()
        {
            // Arrange: a ready+eligible+measurable row that title-matches the wish, seeded in a
            // library the live safe scope covers — e.g. the seeded "Please Stand By" loop, which the
            // operator structurally cannot never-play (gh-#99).
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "req-probe-in-scope");
            await InsertSelectableTrackInLibraryAsync(
                db, safeLibraryId, "/req/safe-scope-title-in-scope.flac", "Station",
                "Please Stand By (Station Default)");

            // Act: probe with the live safe scope covering that library.
            var found = await new RequestCatalogProbeRepository(
                    db.DataSource, new FakeSafeScopeProvider(safeLibraryId), NullLogger<RequestCatalogProbeRepository>.Instance)
                .FindBestAsync(null, "please stand by", CancellationToken.None);

            // Assert: null — a listener request must not be able to reach content the operator has
            // no recourse to never-play.
            Assert.Null(found);
        }

        [Fact]
        public async Task TheSameRowMatchesWhenTheSafeScopeIsEmpty()
        {
            // Arrange: the identical row/wish as above, still seeded in a safe-named library — but
            // no safe scope is actually configured this time (the pre-#99 behavior).
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "req-probe-empty-scope");
            var safeMediaId = await InsertSelectableTrackInLibraryAsync(
                db, safeLibraryId, "/req/safe-scope-title-empty-scope.flac", "Station",
                "Please Stand By (Station Default)");

            // Act: probe with no safe scope configured.
            var found = await new RequestCatalogProbeRepository(
                    db.DataSource, new FakeSafeScopeProvider(), NullLogger<RequestCatalogProbeRepository>.Instance)
                .FindBestAsync(null, "please stand by", CancellationToken.None);

            // Assert: the row matches — proving the safe scope, not something else about the row,
            // was doing the excluding above.
            Assert.Equal(safeMediaId, found);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — GetSelectableByIdAsync (SPEC F87.6, STORY-227, PLAN T90)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetSelectableById(DatabaseFixture db)
    {
        [Fact]
        public async Task ASelectableRowIsReturnedWhenNoEnvelopeIsSupplied()
        {
            // Arrange: a plain ready+measurable+eligible row.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/selectable-bypass.flac", "Led Zeppelin", "Kashmir");

            // Act: the OverrideEnvelope=true bypass path — no envelope constraint at all.
            var found = await Probe(db).GetSelectableByIdAsync(id, envelope: null, CancellationToken.None);

            // Assert: the row comes back.
            Assert.Equal(id.ToString(), found?.MediaId);
        }

        [Fact]
        public async Task ANeverPlayRowIsNeverSelectableRegardlessOfEnvelope()
        {
            // Arrange: the T89-matched row was since flagged never_play by an operator.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/selectable-veto.flac", "Led Zeppelin", "Kashmir");
            await RatingRepo(db).SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            // Act.
            var found = await Probe(db).GetSelectableByIdAsync(id, envelope: null, CancellationToken.None);

            // Assert: null — the veto is law regardless of OverrideEnvelope.
            Assert.Null(found);
        }

        [Fact]
        public async Task ARowOutsideASuppliedEnvelopeIsExcluded()
        {
            // Arrange: a Jazz row, and an envelope admitting only Rock.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/selectable-off-envelope.flac", "Miles Davis", "So What");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set genre = 'Jazz' where id = @id", new { id });
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

            // Act: the OverrideEnvelope=false path — envelope supplied.
            var found = await Probe(db).GetSelectableByIdAsync(id, envelope, CancellationToken.None);

            // Assert: null — the row's genre doesn't satisfy the supplied envelope.
            Assert.Null(found);
        }

        [Fact]
        public async Task ARowInsideASuppliedEnvelopeIsSelectable()
        {
            // Arrange: a Rock row and an envelope admitting Rock.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/selectable-in-envelope.flac", "Led Zeppelin", "Kashmir");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set genre = 'Rock' where id = @id", new { id });
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

            // Act.
            var found = await Probe(db).GetSelectableByIdAsync(id, envelope, CancellationToken.None);

            // Assert: the row comes back.
            Assert.Equal(id.ToString(), found?.MediaId);
        }

        [Fact]
        public async Task ARowInsideTheLiveSafeScopeIsNeverSelectable()
        {
            // Arrange: an otherwise-selectable row seeded in a library the live safe scope covers.
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "selectable-safe-scope");
            var id = await InsertSelectableTrackInLibraryAsync(
                db, safeLibraryId, "/req/selectable-safe-scope.flac", "Station", "Please Stand By");

            // Act: probe with the live safe scope covering that library.
            var found = await new RequestCatalogProbeRepository(
                    db.DataSource, new FakeSafeScopeProvider(safeLibraryId), NullLogger<RequestCatalogProbeRepository>.Instance)
                .GetSelectableByIdAsync(id, envelope: null, CancellationToken.None);

            // Assert: null — safe content is never requestable.
            Assert.Null(found);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — FindVibeAsync (SPEC F87.6, STORY-227, PLAN T90)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFindVibe(DatabaseFixture db)
    {
        [Fact]
        public async Task ARowSharingAMoodIsFound()
        {
            // Arrange: a selectable row tagged with a mood the request also names.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/vibe-match.flac", "Some Artist", "Some Song");
            await Harness.Repo(db).WriteMoodsAsync(id, ["dreamy"], CancellationToken.None);

            // Act: the OverrideEnvelope=true bypass path.
            var found = await Probe(db).FindVibeAsync(["dreamy"], envelope: null, CancellationToken.None);

            // Assert: the tagged row comes back.
            Assert.Equal(id.ToString(), found?.MediaId);
        }

        [Fact]
        public async Task ARowWithNoOverlappingMoodIsNotFound()
        {
            // Arrange: a selectable row tagged with an unrelated mood.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/vibe-no-overlap.flac", "Some Artist", "Some Song");
            await Harness.Repo(db).WriteMoodsAsync(id, ["triumphant"], CancellationToken.None);

            // Act.
            var found = await Probe(db).FindVibeAsync(["dreamy"], envelope: null, CancellationToken.None);

            // Assert: null — no mood overlap.
            Assert.Null(found);
        }

        [Fact]
        public async Task ANeverPlayRowIsNeverAVibeMatch()
        {
            // Arrange: a mood-tagged row flagged never_play.
            await db.ResetAsync();
            var id = await InsertSelectableTrackAsync(db, "/req/vibe-veto.flac", "Some Artist", "Some Song");
            await Harness.Repo(db).WriteMoodsAsync(id, ["dreamy"], CancellationToken.None);
            await RatingRepo(db).SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            // Act.
            var found = await Probe(db).FindVibeAsync(["dreamy"], envelope: null, CancellationToken.None);

            // Assert: null — the veto is law regardless of OverrideEnvelope.
            Assert.Null(found);
        }

        [Fact]
        public async Task ARowInsideTheLiveSafeScopeIsNeverAVibeMatch()
        {
            // Arrange: a mood-tagged row seeded in a library the live safe scope covers.
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "vibe-safe-scope");
            var id = await InsertSelectableTrackInLibraryAsync(
                db, safeLibraryId, "/req/vibe-safe-scope.flac", "Station", "Please Stand By");
            await Harness.Repo(db).WriteMoodsAsync(id, ["dreamy"], CancellationToken.None);

            // Act: probe with the live safe scope covering that library.
            var found = await new RequestCatalogProbeRepository(
                    db.DataSource, new FakeSafeScopeProvider(safeLibraryId), NullLogger<RequestCatalogProbeRepository>.Instance)
                .FindVibeAsync(["dreamy"], envelope: null, CancellationToken.None);

            // Assert: null — safe content is never requestable.
            Assert.Null(found);
        }
    }
}
