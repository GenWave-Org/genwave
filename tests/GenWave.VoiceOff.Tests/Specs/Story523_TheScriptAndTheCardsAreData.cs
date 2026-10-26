// STORY-523 — The script and the cards are data (gh-#614 · SPEC F217 · PLAN T661)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheScriptAndTheCardsAreData
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the script loads
    public sealed class ScenarioTheScriptLoads
    {
        // Given tools/VoiceOff/script/script.json
        // When  it is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItHoldsEightParagraphs() => Assert.Fail("pending: T661 — it holds eight paragraphs");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsVersionIsV2() => Assert.Fail("pending: T661 — its version is v2");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void EveryParagraphHasA1BasedNumberAndNonEmptyText() => Assert.Fail("pending: T661 — every paragraph has a 1-based number and non-empty text");
    }

    // AC2 — the script hash is stable
    public sealed class ScenarioTheScriptHashIsStable
    {
        // Given the script file
        // When  it is loaded twice

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void BothLoadsReportTheSameSHA256Hash() => Assert.Fail("pending: T661 — both loads report the same SHA-256 hash");
    }

    // AC3 — the script hash moves with the text
    public sealed class ScenarioTheScriptHashMovesWithTheText
    {
        // Given the script with one character changed
        // When  it is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void TheHashDiffersFromTheShippedHash() => Assert.Fail("pending: T661 — the hash differs from the shipped hash");
    }

    // AC4 — the cue vocabulary covers the script
    public sealed class ScenarioTheCueVocabularyCoversTheScript
    {
        // Given the seven cues breath, laugh, um, cough, giggle, sigh and pause
        // When  the script's cue tokens are listed

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void EveryTokenIsOneOfTheSeven() => Assert.Fail("pending: T661 — every token is one of the seven");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void NoOtherTokenShapeAppears() => Assert.Fail("pending: T661 — no other token shape appears");
    }

    // AC5 — a card loads
    public sealed class ScenarioACardLoads
    {
        // Given a card file with engine, host, voice, speed, settings, cues, overrides, facts and why
        // When  it is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsEngineNamesAProfileInEngines() => Assert.Fail("pending: T661 — its engine names a profile in engines/");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsHostIsOneOfThorGpuThorCpuRunesGpu() => Assert.Fail("pending: T661 — its host is one of thor-gpu, thor-cpu, runes-gpu");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsSettingsObjectIsKeptVerbatim() => Assert.Fail("pending: T661 — its settings object is kept verbatim");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsFactsCarryACannotList() => Assert.Fail("pending: T661 — its facts carry a cannot list");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ItsWhyIsNonEmpty() => Assert.Fail("pending: T661 — its why is non-empty");
    }

    // AC6 — a variant names its sibling
    public sealed class ScenarioAVariantNamesItsSibling
    {
        // Given a card with variantOf
        // When  the card set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void TheSiblingIsACardInTheSameSet() => Assert.Fail("pending: T661 — the sibling is a card in the same set");
    }

    // AC7 — the shipped card set is complete
    public sealed class ScenarioTheShippedCardSetIsComplete
    {
        // Given tools/VoiceOff/cards
        // When  the set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ThereIsAtLeastOneCardPerProfileInEngines() => Assert.Fail("pending: T661 — there is at least one card per profile in engines/");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void ExactlyOneCardIsMarkedAsShipped() => Assert.Fail("pending: T661 — exactly one card is marked asShipped");

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void TheFiveVariantCardsNamedInPROJECTMdArePresent() => Assert.Fail("pending: T661 — the five variant cards named in PROJECT.md are present");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — an unknown engine is refused
    public sealed class ScenarioAnUnknownEngineIsRefused
    {
        // Given a card naming an engine with no profile
        // When  the set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void LoadingFailsNamingTheCardId() => Assert.Fail("pending: T661 — loading fails naming the card id");
    }

    // AC9 — an unknown host is refused
    public sealed class ScenarioAnUnknownHostIsRefused
    {
        // Given a card with host laptop
        // When  the set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void LoadingFailsNamingTheCardIdAndTheHost() => Assert.Fail("pending: T661 — loading fails naming the card id and the host");
    }

    // AC10 — an unknown cue key is refused
    public sealed class ScenarioAnUnknownCueKeyIsRefused
    {
        // Given a card whose cues map a key hiccup
        // When  the set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void LoadingFailsNamingTheKey() => Assert.Fail("pending: T661 — loading fails naming the key");
    }

    // AC11 — a duplicate id is refused
    public sealed class ScenarioADuplicateIdIsRefused
    {
        // Given two card files sharing an id
        // When  the set is loaded

        [Fact(Skip = "pending: T661 — see docs/PLAN.md")]
        public void LoadingFailsNamingTheId() => Assert.Fail("pending: T661 — loading fails naming the id");
    }
}
