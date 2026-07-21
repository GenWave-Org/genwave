// STORY-209 — Card import lands safely on a stranger's station
//
// BDD specification — xUnit (SPEC F79.2, F79.3, F79.4, F79.6, F79.7). PLAN T67 wires the import
// endpoint; T69 is the cross-station round-trip proof. Entry-point scenarios drive the real
// admin import route (WebApplicationFactory) — fail-closed means the HTTP surface rejects and
// the DB is provably untouched.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePersonaCardImport
{
    public static class ScenarioFreshImport
    {
        // Arrange (T67/T69): an export produced from a seeded "station A" persona (character,
        // voice, pronunciations, signature taste) imported into a fresh station via the API.

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSameCharacterFields()
        {
            // soul/quirks/tagline byte-equal after round-trip (F79.7)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSameVoiceSettings()
        {
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesThePronunciations()
        {
            // card corrections present and merged UNDER station rules (F71.7 unchanged)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSignatureTaste()
        {
            // authored taste rows exist with source='authored' (F79.3)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaHasEmptyAccruedMemory()
        {
            Assert.Fail("pending T69");
        }
    }

    public static class ScenarioReImportOntoALivingPersona
    {
        // Arrange (T67): a persona with accrued memory + accrued taste; import an updated
        // card with the same slug.

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void CardFieldsAreReplacedByTheUpdate()
        {
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void AuthoredRowsAreUpserted()
        {
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void EveryAccruedRowSurvivesUntouched()
        {
            // memory AND taste accrued rows byte-identical before/after (F79.3)
            Assert.Fail("pending T67");
        }
    }

    public static class ScenarioUnknownVoiceResolution
    {
        // Arrange (T67): card naming voice engine/voiceId this station lacks.

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void ImportSucceedsWithTheStationDefaultVoice()
        {
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void AVisibleWarningNamesTheUnresolvedVoice()
        {
            // response carries the warning; persona state exposes it for the UI (F79.4)
            Assert.Fail("pending T67");
        }
    }

    public static class SadPathFailClosedValidation
    {
        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void NewerSchemaMajorIsRejectedNamingBothVersions()
        {
            // message contains card major and station major (F79.2)
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void OversizedPayloadIsRejected()
        {
            // > 256 KB ⇒ rejected (F79.6)
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void MalformedPayloadIsRejected()
        {
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void BadSlugFormatIsRejected()
        {
            Assert.Fail("pending T67");
        }

        [Fact(Skip = "Pending T67 — see docs/PLAN.md")]
        public static void ARejectedImportChangesNothing()
        {
            // persona/memory/taste tables byte-identical after any rejection above (F79.6 transactional)
            Assert.Fail("pending T67");
        }
    }
}
