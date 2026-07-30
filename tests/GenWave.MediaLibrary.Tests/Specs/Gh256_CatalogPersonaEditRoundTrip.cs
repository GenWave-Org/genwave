// gh-#256 — admin-ui: editing a catalog-hired DJ must neither hide nor wipe its card.
//
// BDD specification — xUnit. Two halves, same file because they pin one rule
// (PersonaRepository.MergeCard — the gh-#256 edit-wipe fix):
//
//   • Pure MergeCard facts (no DB): which card fields an admin edit may touch (name, voiceId,
//     soul) and which it must NEVER reset (quirks, lore, tagline, corrections, energy disposition,
//     the VoiceSpec's engine/pace/language). The pre-fix UpdateAsync rebuilt the whole definition
//     from the legacy four-field draft, so ANY admin edit of a hired persona — even just a voice
//     change — silently emptied all of those.
//
//   • Postgres-backed round trip (Category=Integration, DatabaseCollection): a real catalog-shaped
//     import (PersonaImportRepository) followed by a real admin UpdateAsync, proving the stored
//     definition's soul/quirks/lore survive the edit — and that a draft carrying Soul edits the
//     card soul verbatim, never relabeled through the legacy Backstory:/Style: rebuild.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureCatalogPersonaEditRoundTrip
{
    const string CatalogSoul =
        "Late Night Lena has broadcast from a converted lighthouse since 1987.\nStyle: hushed, confiding, never hurried.";

    static PersonaCard CatalogCard(string name = "Late Night Lena") => new(
        SchemaVersion: PersonaCard.CurrentSchemaVersion,
        Name: name,
        Tagline: "The voice at the edge of the dial",
        Soul: CatalogSoul,
        Quirks: ["Always mentions the weather at sea", "Collects broken transistor radios"],
        Voice: new VoiceSpec(Engine: "kokoro", VoiceId: "af_heart", Pace: 0.9, Language: "en"),
        EnergyDisposition: -0.4,
        Lore: ["Once kept the signal alive through a three-day storm on a car battery"],
        Corrections: []);

    // ---------------------------------------------------------------------
    // Pure merge rules — no database in the arrange.
    // ---------------------------------------------------------------------

    public sealed class ScenarioMergeRules
    {
        [Fact]
        public void AVoiceOnlyEditPreservesEveryCardFieldTheDraftDoesNotCarry()
        {
            var existing = CatalogCard();
            var draft = new PersonaDraft("Late Night Lena", Backstory: "", Style: "", Voice: "af_alloy");

            var merged = PersonaRepository.MergeCard(existing, draft);

            Assert.Equal(CatalogSoul, merged.Soul);
            Assert.Equal(existing.Quirks, merged.Quirks);
            Assert.Equal(existing.Lore, merged.Lore);
            Assert.Equal(existing.Tagline, merged.Tagline);
            Assert.Equal(existing.EnergyDisposition, merged.EnergyDisposition);
            Assert.Equal(existing.Corrections, merged.Corrections);
            // The VoiceSpec updates ONLY its voiceId — engine/pace/language are card content.
            Assert.Equal("af_alloy", merged.Voice.VoiceId);
            Assert.Equal("kokoro", merged.Voice.Engine);
            Assert.Equal(0.9, merged.Voice.Pace);
            Assert.Equal("en", merged.Voice.Language);
        }

        [Fact]
        public void ADraftCarryingSoulEditsTheSoulVerbatim()
        {
            var existing = CatalogCard();
            var editedSoul = CatalogSoul + "\nNow broadcasting from a houseboat.";
            var draft = new PersonaDraft("Late Night Lena", "", "", "af_heart", Soul: editedSoul);

            var merged = PersonaRepository.MergeCard(existing, draft);

            Assert.Equal(editedSoul, merged.Soul); // verbatim — never relabeled "Backstory: …".
            Assert.Equal(existing.Quirks, merged.Quirks);
            Assert.Equal(existing.Lore, merged.Lore);
        }

        [Fact]
        public void ALegacyEditWithNonEmptyBackstoryStillRebuildsTheSoulFromTheLegacyFields()
        {
            // An authored persona's soul has always been the labeled Backstory/Style composition —
            // that contract (STORY-192's zero-prompt-change guarantee) must survive the merge fix.
            var existing = LegacyPersonaCardMapper.BuildCard("Rex", "Old story", "Old style", "af_alloy");
            var draft = new PersonaDraft("Rex", "New story", "New style", "af_alloy");

            var merged = PersonaRepository.MergeCard(existing, draft);

            Assert.Equal("Backstory: New story\nStyle: New style", merged.Soul);
        }

        [Fact]
        public void AnEmptyLegacyRebuildStillPreservesAnExistingSoul()
        {
            // The T37 bootstrap-row guard, re-proven against the merge: empty draft fields never
            // blank a non-empty existing soul.
            var existing = CatalogCard();
            var draft = new PersonaDraft("Late Night Lena", "", "", "af_heart");

            var merged = PersonaRepository.MergeCard(existing, draft);

            Assert.Equal(CatalogSoul, merged.Soul);
        }

        [Fact]
        public void ARowWithNoReconciledCardFallsBackToTheFullLegacyBuild()
        {
            var draft = new PersonaDraft("Rex", "Story", "Style", "af_alloy");

            var merged = PersonaRepository.MergeCard(existing: null, draft);

            Assert.Equal("Backstory: Story\nStyle: Style", merged.Soul);
            Assert.Empty(merged.Quirks);
            Assert.Empty(merged.Lore);
        }
    }

    // ---------------------------------------------------------------------
    // Real hire → real edit → the card survives, at the actual table.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioHiredPersonaSurvivesAnAdminEdit(DatabaseFixture db)
    {
        static PersonaRepository Repo(DatabaseFixture db) =>
            new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

        static PersonaImportRepository ImportRepo(DatabaseFixture db) =>
            new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

        /// <summary>Re-applies the card (db/11) and provenance (db/25) migrations before arranging —
        /// Story118's pre-T2 recreate scenario legitimately rebuilds <c>station.persona</c> via
        /// db/09+db/11 only, leaving <c>imported_from</c>/<c>imported_at</c> absent for any spec
        /// that runs after it in the shared collection. Idempotent scripts, same defensive-arrange
        /// convention Story237's own migration scenarios already follow.</summary>
        void EnsureImportSchema()
        {
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "11-persona-card-migration.sh"));
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "25-persona-provenance-migration.sh"));
        }

        async Task<long> HireAsync()
        {
            EnsureImportSchema();
            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest("late-night-lena", LegacyVoice: "af_heart", CatalogCard(), "late-night-lena"),
                CancellationToken.None);
            var imported = Assert.IsType<PersonaImportOutcome.Imported>(outcome);
            return imported.PersonaId;
        }

        [Fact]
        public async Task AVoiceChangeThroughUpdateAsyncLeavesSoulQuirksAndLoreIntact()
        {
            await db.ResetStationAsync();
            var personaId = await HireAsync();
            var repo = Repo(db);

            var result = await repo.UpdateAsync(
                personaId, new PersonaDraft("Late Night Lena", "", "", "af_alloy"), CancellationToken.None);

            Assert.IsType<PersonaWriteResult.Updated>(result);
            var card = await repo.GetCardByIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(card);
            Assert.Equal(CatalogSoul, card.Soul);
            Assert.Equal(CatalogCard().Quirks, card.Quirks);
            Assert.Equal(CatalogCard().Lore, card.Lore);
            Assert.Equal("The voice at the edge of the dial", card.Tagline);
            Assert.Equal("af_alloy", card.Voice.VoiceId);
            Assert.Equal("kokoro", card.Voice.Engine);
        }

        [Fact]
        public async Task ASoulEditRoundTripsVerbatimWhileQuirksAndLoreSurvive()
        {
            await db.ResetStationAsync();
            var personaId = await HireAsync();
            var repo = Repo(db);
            var editedSoul = CatalogSoul + "\nNow broadcasting from a houseboat.";

            var result = await repo.UpdateAsync(
                personaId,
                new PersonaDraft("Late Night Lena", "", "", "af_heart", Soul: editedSoul),
                CancellationToken.None);

            Assert.IsType<PersonaWriteResult.Updated>(result);
            var card = await repo.GetCardByIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(card);
            Assert.Equal(editedSoul, card.Soul);
            Assert.Equal(CatalogCard().Quirks, card.Quirks);
            Assert.Equal(CatalogCard().Lore, card.Lore);
        }

        [Fact]
        public async Task GetCardsAsyncReturnsTheHiredCardKeyedById()
        {
            await db.ResetStationAsync();
            var personaId = await HireAsync();
            var repo = Repo(db);

            var cards = await repo.GetCardsAsync(CancellationToken.None);

            Assert.True(cards.ContainsKey(personaId));
            Assert.Equal(CatalogSoul, cards[personaId].Soul);
        }
    }
}
