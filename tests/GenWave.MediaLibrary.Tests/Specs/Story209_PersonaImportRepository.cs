// STORY-209 — Cross-station round-trip proof at the repository layer (T69)
//
// BDD specification — xUnit (SPEC F79.3, F79.6, F79.7). Postgres-backed (Category=Integration, shared
// DatabaseFixture) — mirrors Story194/Story213's own "a fake would never exercise this honestly"
// rationale, here for THREE seams T66/T67 shipped with no real-Postgres coverage at all:
// PersonaRepository.GetIdBySlugAsync, PersonaMemoryRepository.ListAsync, and PersonaImportRepository's
// own transactional SQL (the F79.6 "rejected/failed import changes nothing" guarantee, until now only
// proven by a REPLICATING in-memory fake in Story209_PersonaImport.cs and a manual curl smoke).
//
// SPLIT (T60 precedent — Host.Tests has no station-Postgres convention; Story215_BoothLogPersonaStamp.cs
// draws the same line): Story209_PersonaImport.cs's 5 ScenarioFreshImport facts drive the real HTTP
// route pair (export → import) through WebApplicationFactory with REPLICATING fakes — proving the
// controller's OWN serialization/validation/request-shape logic round-trips correctly. THIS file proves
// the write path underneath actually has the SQL teeth the fakes merely simulate: the real
// PersonaImportRepository/PersonaRepository/PersonaMemoryRepository/PersonaTasteRepository, driven with
// the exact PersonaImportRequest shape PersonaController.Import builds, against real Postgres.
//
// This harness has ONE physical station schema (no second "station B" database to stand up per test).
// db.ResetStationAsync() between "station A"'s arrangement and the import stands in for a fresh,
// unrelated station — CASCADE-truncating station.persona wipes every row (and, via FK CASCADE, every
// persona_memory/persona_taste row) it owned, so the import genuinely lands on empty tables rather than
// merely a different row in the same ones.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaImportRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static PersonaImportRepository ImportRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static PersonaRepository PersonaRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static PersonaMemoryRepository MemoryRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions()));

    static PersonaTasteRepository TasteRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    /// <summary>Every card field populated — mirrors Story209_PersonaImport.cs's own PersonaImportFixture.BuildCard.</summary>
    static PersonaCard BuildCard(
        string tagline = "Late-night gravity.",
        string voiceId = "af_heart",
        IReadOnlyList<string>? lore = null,
        IReadOnlyList<TasteRule>? taste = null) =>
        new(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: "DJ Nova",
            Tagline: tagline,
            Soul: "A slow-orbit voice for the small hours.",
            Quirks: ["Never says goodnight twice."],
            Voice: new VoiceSpec(Engine: "kokoro", VoiceId: voiceId, Pace: 1.0, Language: "en"),
            EnergyDisposition: -0.2,
            Lore: lore ?? ["Once got lost inside a 20-minute Tangerine Dream fade."],
            Corrections: [new PersonaCorrection("Nova", "NOH-vah")],
            Taste: taste ?? [SignatureRule]);

    static readonly TasteRule SignatureRule =
        new(new TastePredicate("Led Zeppelin", null, null), new TasteContext([DayOfWeek.Sunday], 6, 12), 0.75);

    /// <summary>
    /// Replicates <c>PersonaController.Export</c> field for field (SPEC F79.1): the stored card with
    /// <c>Lore</c>/<c>Taste</c> REPLACED by a fresh, source-filtered read of persona_memory/persona_taste
    /// (authored only) — the exact shape the admin export route hands back, built here from the SAME
    /// real repositories the controller itself uses (<see cref="PersonaRepository.GetCardByIdAsync"/>,
    /// <see cref="PersonaMemoryRepository.ListAsync"/>, <see cref="PersonaTasteRepository.ListAsync"/>).
    /// </summary>
    static async Task<PersonaCard> ExportCardAsync(DatabaseFixture db, long personaId, CancellationToken ct)
    {
        var card = await PersonaRepo(db).GetCardByIdAsync(personaId, ct);
        Assert.NotNull(card);

        var lore = await MemoryRepo(db).ListAsync(personaId, PersonaMemorySource.Authored, ct);
        var taste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Authored, ct);

        return card with
        {
            Lore = lore.Select(entry => entry.Content).ToArray(),
            Taste = taste.Select(entry => entry.Rule).ToArray(),
        };
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the F79.7 acceptance sentence, verbatim, at the repository layer
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCrossStationRoundTrip(DatabaseFixture db)
    {
        const string Slug = "dj-nova";

        /// <summary>
        /// Arranges "station A": a living persona carrying the full round-trip card PLUS one accrued
        /// memory row and one accrued taste row (F79.1's "a persona that has both" shape) — built
        /// through <see cref="PersonaImportRepository.ImportAsync"/> itself (the same write path the
        /// admin import route uses to create a persona in the first place), not a hand-rolled SQL
        /// insert, so the export step below starts from data this very repository is known to write
        /// correctly.
        /// </summary>
        async Task<long> SeedStationAAsync()
        {
            await db.ResetStationAsync();

            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest(Slug, LegacyVoice: "af_heart", BuildCard()), CancellationToken.None);
            var personaId = Assert.IsType<PersonaImportOutcome.Imported>(outcome).PersonaId;

            await MemoryRepo(db).RecordAsync(
                personaId, "bit", "Accrued bit that must never re-export.", PersonaMemorySource.Accrued, CancellationToken.None);
            await TasteRepo(db).InsertAsync(
                personaId,
                new TasteRule(new TastePredicate("Nickelback", null, null), new TasteContext([], null, null), -0.9),
                PersonaTasteSource.Accrued, CancellationToken.None);

            return personaId;
        }

        /// <summary>
        /// Station A → export → reset (stands in for "a fresh station") → import, using the exact
        /// <see cref="PersonaImportRequest"/> shape <c>PersonaController.Import</c> builds: the route's
        /// own slug, a resolved legacy voice (simulating F79.4 finding the card's voiceId on the new
        /// station's engine — the RESOLUTION check itself is <c>PersonaController</c>'s job, covered by
        /// Story209_PersonaImport.cs's ScenarioUnknownVoiceResolution), and the exported card verbatim.
        /// </summary>
        async Task<long> ExportFromAAndImportToFreshBAsync()
        {
            var stationAId = await SeedStationAAsync();
            var exportCard = await ExportCardAsync(db, stationAId, CancellationToken.None);

            await db.ResetStationAsync();

            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest(Slug, LegacyVoice: exportCard.Voice.VoiceId, exportCard), CancellationToken.None);

            return Assert.IsType<PersonaImportOutcome.Imported>(outcome).PersonaId;
        }

        [Fact]
        public async Task TheImportedPersonaIsAddressableByItsSlug()
        {
            var personaId = await ExportFromAAndImportToFreshBAsync();

            Assert.Equal(personaId, await PersonaRepo(db).GetIdBySlugAsync(Slug, CancellationToken.None));
        }

        [Fact]
        public async Task ImportedPersonaCarriesTheSameCharacterFields()
        {
            var personaId = await ExportFromAAndImportToFreshBAsync();
            var card = await PersonaRepo(db).GetCardByIdAsync(personaId, CancellationToken.None);

            Assert.NotNull(card);
            Assert.Equal("DJ Nova", card.Name);
            Assert.Equal("Late-night gravity.", card.Tagline);
            Assert.Equal("A slow-orbit voice for the small hours.", card.Soul);
            Assert.Equal(["Never says goodnight twice."], card.Quirks);
        }

        [Fact]
        public async Task ImportedPersonaCarriesTheSameVoiceSettings()
        {
            var personaId = await ExportFromAAndImportToFreshBAsync();
            var card = await PersonaRepo(db).GetCardByIdAsync(personaId, CancellationToken.None);
            var persona = await PersonaRepo(db).GetByIdAsync(personaId, CancellationToken.None);

            Assert.NotNull(card);
            Assert.Equal(new VoiceSpec("kokoro", "af_heart", 1.0, "en"), card.Voice);

            Assert.NotNull(persona);
            Assert.Equal("af_heart", persona.Voice); // the legacy column import resolved (F79.4)
        }

        [Fact]
        public async Task ImportedPersonaCarriesThePronunciations()
        {
            var personaId = await ExportFromAAndImportToFreshBAsync();
            var card = await PersonaRepo(db).GetCardByIdAsync(personaId, CancellationToken.None);

            Assert.NotNull(card);
            Assert.Equal([new PersonaCorrection("Nova", "NOH-vah")], card.Corrections);
        }

        [Fact]
        public async Task ImportedPersonaCarriesTheSignatureTaste()
        {
            var personaId = await ExportFromAAndImportToFreshBAsync();
            var taste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Authored, CancellationToken.None);

            var entry = Assert.Single(taste);
            Assert.Equal(PersonaTasteSource.Authored, entry.Source);
            Assert.Equal("Led Zeppelin", entry.Rule.Predicate.Artist);
            Assert.Equal(0.75, entry.Rule.Weight);
            Assert.Equal([DayOfWeek.Sunday], entry.Rule.Context.DaysOfWeek);
        }

        [Fact]
        public async Task ImportedPersonaHasEmptyAccruedMemoryAndTaste()
        {
            // F79.7 verbatim: "...with an empty accrued memory." Station A's accrued rows never even
            // reach the export card (PersonaCard has no field for one — proven independently by
            // Story208_PersonaExport.cs's ExportContainsZeroAccruedRowsOfAnyKind), so this fact is
            // really about the DESTINATION: db.ResetStationAsync() above is what makes station B
            // genuinely fresh rather than merely "the same row before its accrued rows were added".
            var personaId = await ExportFromAAndImportToFreshBAsync();

            var accruedMemory = await MemoryRepo(db).ListAsync(personaId, PersonaMemorySource.Accrued, CancellationToken.None);
            var accruedTaste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None);
            var operatorTaste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Operator, CancellationToken.None);

            Assert.Empty(accruedMemory);
            Assert.Empty(accruedTaste);
            Assert.Empty(operatorTaste);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — re-import onto a living persona (carried T67 review gate: operator rows too)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReImportPreservesAccruedAndOperatorRows(DatabaseFixture db)
    {
        const string Slug = "dj-nova-living";

        static readonly TasteRule OldAuthoredRule =
            new(new TastePredicate("Old Artist", null, null), new TasteContext([], null, null), 0.1);
        static readonly TasteRule NewAuthoredRule =
            new(new TastePredicate("New Artist", null, null), new TasteContext([], null, null), 0.4);
        static readonly TasteRule AccruedRule =
            new(new TastePredicate("Nickelback", null, null), new TasteContext([], null, null), -0.9);
        static readonly TasteRule OperatorRule =
            new(new TastePredicate(null, "Vaporwave", null), new TasteContext([], null, null), 0.3);

        /// <summary>Seeds an OLD authored card plus BOTH an accrued and an operator taste row, and one accrued memory row.</summary>
        async Task<long> SeedLivingPersonaAsync()
        {
            await db.ResetStationAsync();

            var oldCard = BuildCard(tagline: "OLD tagline", lore: ["OLD lore"], taste: [OldAuthoredRule]);
            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest(Slug, "af_heart", oldCard), CancellationToken.None);
            var personaId = Assert.IsType<PersonaImportOutcome.Imported>(outcome).PersonaId;

            await MemoryRepo(db).RecordAsync(personaId, "bit", "Accrued bit must survive.", PersonaMemorySource.Accrued, CancellationToken.None);
            await TasteRepo(db).InsertAsync(personaId, AccruedRule, PersonaTasteSource.Accrued, CancellationToken.None);
            await TasteRepo(db).InsertAsync(personaId, OperatorRule, PersonaTasteSource.Operator, CancellationToken.None);

            return personaId;
        }

        [Fact]
        public async Task CardFieldsAndAuthoredRowsAreReplacedByTheUpdate()
        {
            var personaId = await SeedLivingPersonaAsync();

            var newCard = BuildCard(tagline: "NEW tagline", lore: ["NEW lore"], taste: [NewAuthoredRule]);
            await ImportRepo(db).ImportAsync(new PersonaImportRequest(Slug, "af_heart", newCard), CancellationToken.None);

            var card = await PersonaRepo(db).GetCardByIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(card);
            Assert.Equal("NEW tagline", card.Tagline);

            var authoredMemory = await MemoryRepo(db).ListAsync(personaId, PersonaMemorySource.Authored, CancellationToken.None);
            Assert.Equal(["NEW lore"], authoredMemory.Select(m => m.Content));

            var authoredTaste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Authored, CancellationToken.None);
            var rule = Assert.Single(authoredTaste);
            Assert.Equal("New Artist", rule.Rule.Predicate.Artist);
        }

        [Fact]
        public async Task EveryAccruedRowSurvivesUntouched()
        {
            var personaId = await SeedLivingPersonaAsync();
            var accruedMemoryBefore = Assert.Single(
                await MemoryRepo(db).ListAsync(personaId, PersonaMemorySource.Accrued, CancellationToken.None));
            var accruedTasteBefore = Assert.Single(
                await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None));

            await ImportRepo(db).ImportAsync(
                new PersonaImportRequest(Slug, "af_heart", BuildCard(tagline: "NEW tagline")), CancellationToken.None);

            var accruedMemoryAfter = Assert.Single(
                await MemoryRepo(db).ListAsync(personaId, PersonaMemorySource.Accrued, CancellationToken.None));
            var accruedTasteAfter = Assert.Single(
                await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None));

            Assert.Equal(accruedMemoryBefore, accruedMemoryAfter);
            Assert.Equal(accruedTasteBefore.Id, accruedTasteAfter.Id);
        }

        [Fact]
        public async Task TheOperatorTasteRowSurvivesUntouched()
        {
            // Carried T67 review gate: import replaces ONLY source='authored' taste rows. An
            // operator-nudged row (a direct admin edit — never part of any card) must survive a
            // re-import exactly like an accrued row does; it is neither "authored" nor "accrued".
            var personaId = await SeedLivingPersonaAsync();
            var operatorBefore = Assert.Single(
                await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Operator, CancellationToken.None));

            await ImportRepo(db).ImportAsync(
                new PersonaImportRequest(Slug, "af_heart", BuildCard(tagline: "NEW tagline")), CancellationToken.None);

            var operatorAfter = Assert.Single(
                await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Operator, CancellationToken.None));
            Assert.Equal(operatorBefore.Id, operatorAfter.Id);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — 409 NameConflict against the real UNIQUE constraint, rollback leaves no partial state
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNameConflictRollsBackWithNoPartialState(DatabaseFixture db)
    {
        [Fact]
        public async Task ImportOfACardWhoseNameCollidesReturnsNameConflictAndWritesNothing()
        {
            await db.ResetStationAsync();
            var existing = Assert.IsType<PersonaWriteResult.Created>(
                await PersonaRepo(db).CreateAsync(new PersonaDraft("Existing Name", "", "", ""), CancellationToken.None));

            var collidingCard = BuildCard() with { Name = "Existing Name" };
            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest("dj-collision", "af_heart", collidingCard), CancellationToken.None);

            Assert.IsType<PersonaImportOutcome.NameConflict>(outcome);

            // No partial write under the attempted slug — the real UNIQUE(name) constraint on
            // station.persona (not a pre-check) is what rejected this, and the whole transaction rolled
            // back rather than leaving a name-less/half-written row behind.
            Assert.Null(await PersonaRepo(db).GetIdBySlugAsync("dj-collision", CancellationToken.None));

            var all = await PersonaRepo(db).GetAllAsync(CancellationToken.None);
            var untouched = Assert.Single(all);
            Assert.Equal(existing.Persona.Id, untouched.Id);
            Assert.Equal("Existing Name", untouched.Name);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a mid-import fault (after the persona upsert already ran) rolls back everything
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMidImportFaultRollsBackTheWholeTransaction(DatabaseFixture db)
    {
        [Fact]
        public async Task AFailureAfterThePersonaUpsertUndoesTheUpsertToo()
        {
            // Honest fault injection — no mocking framework, no exception-injection seam: a null lore
            // entry reaches station.persona_memory.content, a real NOT NULL column. Unlike
            // TasteRule.Weight, PersonaCard.Lore has no validating constructor of its own (that
            // boundary check is PersonaController's deserialization gate, GenWave.Host.Tests
            // territory) — this spec is honestly reachable at the repository layer precisely because
            // nothing upstream of it stops a null from arriving. The first lore entry ("valid lore")
            // commits its own INSERT inside the transaction before the second (null) one throws, so
            // this also proves a PARTIALLY-written multi-row loop still rolls back in full, including
            // the persona row itself that was upserted moments earlier in the SAME transaction.
            await db.ResetStationAsync();

            var badLore = new List<string> { "valid lore", null! };
            var card = BuildCard() with { Lore = badLore };

            await Assert.ThrowsAsync<PostgresException>(() => ImportRepo(db).ImportAsync(
                new PersonaImportRequest("dj-fault", "af_heart", card), CancellationToken.None));

            Assert.Null(await PersonaRepo(db).GetIdBySlugAsync("dj-fault", CancellationToken.None));
            Assert.Empty(await PersonaRepo(db).GetAllAsync(CancellationToken.None));

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var leaked = await conn.ExecuteScalarAsync<long>(
                "select count(*) from station.persona_memory where content = 'valid lore'");
            Assert.Equal(0, leaked);
        }
    }
}
