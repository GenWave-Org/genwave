// STORY-490 — The gauntlet speaks the station's text (gh-#614 · SPEC F214.2, F214.3 · PLAN T607, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheGauntletSpeaksTheStationsText
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — every class is present
    public sealed class ScenarioEveryClassIsPresent
    {
        // Given tools/VoiceOff/gauntlet/*.json
        // When  the gauntlet is loaded

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void ItHoldsAtLeastOneLineOfEachClassLeadInBackAnnounceStationIdPronunciationEmotiveN() => Assert.Fail("pending: T607 — it holds at least one line of each class LeadIn, BackAnnounce, StationId, Pronunciation, Emotive, NonVerbal, Crosstalk");
    }

    // AC2 — the voiced classes cover every register
    public sealed class ScenarioTheVoicedClassesCoverEveryRegister
    {
        // Given the loaded gauntlet
        // When  LeadIn, BackAnnounce and StationId lines are grouped by register

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void EachClassHasLinesForBubblyLaconicAndLateNight() => Assert.Fail("pending: T607 — each class has lines for bubbly, laconic and late-night");
    }

    // AC3 — ids are stable and unique
    public sealed class ScenarioIdsAreStableAndUnique
    {
        // Given the loaded gauntlet
        // When  line ids are listed

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void NoIdRepeats() => Assert.Fail("pending: T607 — no id repeats");
    }

    // AC4 — the hash pins the content
    public sealed class ScenarioTheHashPinsTheContent
    {
        // Given the same gauntlet files loaded twice
        // When  the content hash is computed

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void BothHashesAreEqual() => Assert.Fail("pending: T607 — both hashes are equal");
    }

    // AC5 — any edit moves the hash
    public sealed class ScenarioAnyEditMovesTheHash
    {
        // Given one line's text changed by one character
        // When  the content hash is computed

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void ItDiffersFromTheOriginalHash() => Assert.Fail("pending: T607 — it differs from the original hash");
    }

    // AC6 — the station's normalizer runs first
    public sealed class ScenarioTheStationSNormalizerRunsFirst
    {
        // Given a line containing a corrected artist name and a correction set that rewrites it
        // When  the line's plain-lane text is built

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void ItEqualsSpeechTextNormalizeTextCorrectionsWithCueMarkersStripped() => Assert.Fail("pending: T607 — it equals SpeechText.Normalize(text, corrections) with cue markers stripped");
    }

    // AC7 — Kokoro's plain lane is what airs
    public sealed class ScenarioKokoroSPlainLaneIsWhatAirs
    {
        // Given a line and Kokoro's profile
        // When  the plain-lane text is built for Kokoro

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void ItEqualsKokoroSpeechMarkupRenderNormalizedTextPronunciationRules06() => Assert.Fail("pending: T607 — it equals KokoroSpeechMarkup.Render(normalized text, pronunciation rules, 0.6)");
    }

    // AC8 — the markup lane maps cues
    public sealed class ScenarioTheMarkupLaneMapsCues
    {
        // Given a NonVerbal line with a laugh cue and a profile mapping laugh → "[laughs]"
        // When  the markup-lane text is built

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void TheCueIsReplacedByLaughs() => Assert.Fail("pending: T607 — the cue is replaced by '[laughs]'");
    }

    // AC9 — a null mapping strips the cue
    public sealed class ScenarioANullMappingStripsTheCue
    {
        // Given a profile mapping sigh → null
        // When  the markup-lane text is built for a line with a sigh cue

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void TheTextContainsNoCueMarker() => Assert.Fail("pending: T607 — the text contains no cue marker");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC10 — short lines are refused
    public sealed class ScenarioShortLinesAreRefused
    {
        // Given a gauntlet line of 5 words
        // When  the gauntlet is loaded

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void LoadingFailsNamingThatLineId() => Assert.Fail("pending: T607 — loading fails naming that line id");
    }

    // AC11 — a duplicate id is refused
    public sealed class ScenarioADuplicateIdIsRefused
    {
        // Given two lines sharing an id
        // When  the gauntlet is loaded

        [Fact(Skip = "pending: T607 — see docs/PLAN.md")]
        public void LoadingFailsNamingTheDuplicateId() => Assert.Fail("pending: T607 — loading fails naming the duplicate id");
    }

    // AC12 — pending corrections are visible
    public sealed class ScenarioPendingCorrectionsAreVisible
    {
        // Given corrections.json with "provenance":"pending"
        // When  render starts

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void ItPrintsALineSayingCorrectionsArePending() => Assert.Fail("pending: T611 — it prints a line saying corrections are pending");
    }
}
