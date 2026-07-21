// STORY-076 — Authored rows enter the catalog directly as ready
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). SPEC F27.1 / F27.2 /
// F27.8. P4 names the seam: IAuthoredCatalogWriter.InsertAuthoredAsync, implemented by MediaRepository
// (the same repository that already implements every other Media* contract). No schema change — every
// column already exists (db/01-library.sh).

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAuthoredInsertSeam
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — insert lands a ready, measured, branded row
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioInsertLandsAReadyMeasuredBrandedRow(DatabaseFixture db)
    {
        [Fact]
        public async Task TheRowIsInStateReady()
        {
            // AC1 — state == 'ready' immediately, no enricher round-trip
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            Assert.Equal("ready", await Harness.StateOfAsync(db, id));
        }

        [Fact]
        public async Task MeasurementsArePersistedAsGiven()
        {
            // AC1 — integrated_lufs / true_peak_dbtp / measurable / cue / energy match the inputs
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var insert = Harness.AuthoredInsert(
                loudness: new LoudnessMeasurement(-16.0, -1.5, true),
                cue: new CuePoints(1.0, 2.0),
                energy: new EnergyPoints(0.4, 0.6));
            var id = await writer.InsertAuthoredAsync(insert, CancellationToken.None);

            var row = await Harness.MeasurementsOfAsync(db, id);
            Assert.Equal(-16.0, row.IntegratedLufs);
            Assert.Equal(-1.5, row.TruePeakDbtp);
            Assert.True(row.Measurable);
            Assert.Equal(1.0, row.CueInSec);
            Assert.Equal(2.0, row.CueOutSec);
            Assert.Equal(0.4, row.IntroEnergy);
            Assert.Equal(0.6, row.OutroEnergy);
        }

        [Fact]
        public async Task BrandTagColumnsAreSet()
        {
            // AC1 — artist / title columns carry the brand values
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var insert = Harness.AuthoredInsert(tags: new AudioTags(Artist: "My Station", Title: "Please Stand By"));
            var id = await writer.InsertAuthoredAsync(insert, CancellationToken.None);

            var (title, artist) = await Harness.TagsOfAsync(db, id);
            Assert.Equal("Please Stand By", title);
            Assert.Equal("My Station", artist);
        }

        [Fact]
        public async Task TagsEditedAtIsSet()
        {
            // AC1 — tags_edited_at non-null: the F18.3 freeze protects the brand from backfill
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            Assert.NotNull(await Harness.TagsEditedAtOfAsync(db, id));
        }

        [Fact]
        public async Task CueAndEnergyAnalyzedAtAreSetSoBackfillNeverClaimsTheRow()
        {
            // F27.1 note in PLAN.md: cue_analyzed_at / energy_analyzed_at / bpm_analyzed_at /
            // year_lookup_at / year_lookup_missed_at MUST be set unconditionally (even when
            // Cue/Energy are null) so the F13/F17/STORY-142/F48.3/F76.2 backfill predicates never
            // re-claim this row. year_lookup_missed_at in particular (X5 review finding, extended
            // STORY-200) is the ACTUAL re-claim gate now — stamping only year_lookup_at would no
            // longer be enough to stop the year-lookup backfill from sending an authored row's
            // station-authored artist/title to MusicBrainz.
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var insert = Harness.AuthoredInsert(cue: null, energy: null);
            var id = await writer.InsertAuthoredAsync(insert, CancellationToken.None);

            var (cueAnalyzedAt, energyAnalyzedAt, bpmAnalyzedAt, yearLookupAt, yearLookupMissedAt) = await Harness.AnalyzedAtOfAsync(db, id);
            Assert.NotNull(cueAnalyzedAt);
            Assert.NotNull(energyAnalyzedAt);
            Assert.NotNull(bpmAnalyzedAt);
            Assert.NotNull(yearLookupAt);
            Assert.NotNull(yearLookupMissedAt);
        }

        [Fact]
        public async Task TheRowPathIsUnderAuthoredRoot()
        {
            // AC1 — path starts with /authored
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var insert = Harness.AuthoredInsert(path: "/authored/probe-076.wav");
            var id = await writer.InsertAuthoredAsync(insert, CancellationToken.None);

            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.StartsWith("/authored", reference.Locator);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the row is a normal row downstream (F27.8)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheRowIsANormalRowDownstream(DatabaseFixture db)
    {
        [Fact]
        public async Task TheRowRoundTripsGetByIdWithAnETag()
        {
            // AC2 — GET-by-id projection includes the xmin version like any row
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);
            var repo = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            var (dto, libraryId) = (await repo.GetByIdWithLibraryAsync(id, CancellationToken.None))
                ?? throw new InvalidOperationException("expected the authored row to round-trip");

            Assert.Equal(id.ToString(), dto.MediaId);
            Assert.Equal(1L, libraryId);
            Assert.False(string.IsNullOrEmpty(dto.Version)); // Version carries the xmin ETag token.
        }

        [Fact]
        public async Task TheRowSatisfiesTheSafeTrackSelectionPredicate()
        {
            // AC2 — GetRandomReadyAsync(scope=[its library]) can return it
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);
            var repo = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Equal(id.ToString(), reference.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown library id
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnknownLibraryIdRejectsTheInsert(DatabaseFixture db)
    {
        [Fact]
        public async Task TheInsertFailsAndWritesNothing()
        {
            // AC3 — FK violation surfaces; no library.media row exists afterwards
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var insert = Harness.AuthoredInsert(path: "/authored/unknown-library.wav", libraryId: 999_999L);

            await Assert.ThrowsAsync<Npgsql.PostgresException>(
                () => writer.InsertAuthoredAsync(insert, CancellationToken.None));

            Assert.Equal(0, await Harness.CountMediaRowsAsync(db));
        }
    }
}
