// STORY-468 — Stage directions never reach the voice (gh-#706 · SPEC F201 · PLAN T552)

namespace GenWave.Tts.Tests.Specs;

public static class FeatureStagedirectionsneverreachthevoice
{
    public sealed class ScenarioLineAwareHygieneStripsStageDirections
    {
        // Given: a parenthetical, a bracketed beat, and an asterisked aside — alone (AC1–AC3) and all
        // three on one line (AC4, the SAME given/expected text SPEC F201.3's own AdScriptWriter spec
        // table pins verbatim) — through ApplyLineAwareHygiene.

        /// <summary>AC1 parentheses stripped · AC2 brackets stripped · AC3 asterisks stripped ·
        /// AC4/F201.3 all three on one line strip together, with the whitespace they leave behind
        /// collapsed and a stranded space before the trailing period tidied away.</summary>
        [Theory]
        [InlineData("ANNOUNCER: (warmly) Come on down today.", "ANNOUNCER: Come on down today.")]
        [InlineData("ANNOUNCER: Come on down [beat] today.", "ANNOUNCER: Come on down today.")]
        [InlineData("ANNOUNCER: Come on down *pause* today.", "ANNOUNCER: Come on down today.")]
        [InlineData(
            "ANNOUNCER: (warmly) Come on down *pause* today [beat].",
            "ANNOUNCER: Come on down today.")]
        public void StripsTheShapeAndCollapsesWhitespace(string raw, string expected) =>
            Assert.Equal(expected, AdScriptWriter.ApplyLineAwareHygiene(raw));
    }

    public sealed class ScenarioALineWithNoShapeIsUntouched
    {
        // PLAN T552 review F1: SpaceBeforePunctuationPattern used to run on every line whether or not a
        // shape was stripped, so it rewrote legitimate copy that never carried a stage direction at all.
        // Given: lines whose own punctuation/spacing is deliberate, carrying no "(…)"/"[…]"/"*…*" shape.

        /// <summary>An untouched line comes back byte for byte — an ellipsis is never collapsed, and a
        /// deliberately spaced colon/period is never re-tidied.</summary>
        [Theory]
        [InlineData("ANNOUNCER: wait ... then go")]
        [InlineData("ANNOUNCER: Remember : call now")]
        [InlineData("ANNOUNCER: 3 . 5 dollars")]
        public void ReturnsTheLineByteForByte(string raw) =>
            Assert.Equal(raw, AdScriptWriter.ApplyLineAwareHygiene(raw));

        /// <summary>AC4/F201.3 still yields exactly the same collapsed result once a real shape is
        /// present, pinning that the T552 F1 fix only skips the tidy when NOTHING was stripped.</summary>
        [Fact]
        public void AC4StillCollapsesWhenAShapeIsPresent() =>
            Assert.Equal(
                "ANNOUNCER: Come on down today.",
                AdScriptWriter.ApplyLineAwareHygiene("ANNOUNCER: (warmly) Come on down *pause* today [beat]."));
    }

    public sealed class ScenarioEmptiedLinesNeverVoteForLeadAnnouncer
    {
        // PLAN T552 review F2: a line emptied by hygiene used to still VOTE in the "nobody is ANNOUNCER
        // -> lead voice" election before being dropped at the final join — so two stage-direction-only
        // VOICE1 lines outvoted a single real VOICE2 line and elected the WRONG voice as ANNOUNCER.
        // Given: two pure-direction VOICE1 lines and one real VOICE2 line — VOICE2 should become
        // ANNOUNCER once the emptied VOICE1 lines are dropped before the election runs.

        /// <summary>The emptied VOICE1 lines never cast a vote; VOICE2 — the only voice left with any
        /// text — becomes ANNOUNCER.</summary>
        [Fact]
        public void TheSurvivingVoiceBecomesAnnouncer() =>
            Assert.Equal(
                "ANNOUNCER: Hi.",
                AdScriptWriter.ApplyLineAwareHygiene("VOICE1: (laughs)\nVOICE1: (sighs)\nVOICE2: Hi."));
    }

    public sealed class ScenarioALineThatIsOnlyADirection
    {
        // Given: "ANNOUNCER: Come on down today.\nVOICE1: (laughs)" — STORY-468's own AC5 writes
        // "VOICE" as shorthand for whichever cast voice carries the line; ApplyLineAwareHygiene's
        // known cast is ANNOUNCER/VOICE1/VOICE2 (SPEC F200.1), so this fact uses VOICE1. An ANNOUNCER
        // line precedes it so the script stays otherwise valid (F160.3 still has its required tag) —
        // proving the drop is scoped to the one emptied line, never the whole script.

        /// <summary>AC5 — the VOICE1 line is gone, not left behind as a bare "VOICE1:"</summary>
        [Fact]
        public void DropsTheEmptiedLine() =>
            Assert.Equal(
                "ANNOUNCER: Come on down today.",
                AdScriptWriter.ApplyLineAwareHygiene("ANNOUNCER: Come on down today.\nVOICE1: (laughs)"));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 lives in tests/GenWave.Ads.Tests/Specs/Story468_ResidueFailsValidation.cs — it needs the REAL
    // GenWave.Ads.AdScriptValidator, which this project cannot reference (L10).
}
