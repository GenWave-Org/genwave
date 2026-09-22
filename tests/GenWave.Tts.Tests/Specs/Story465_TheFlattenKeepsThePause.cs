// STORY-465 — The flatten keeps the pause (gh-#703 · SPEC F198 · PLAN T546 T547)
//
// BDD specification — xUnit. RED at plan time: every fact was [Fact(Skip = Pending)] with a loud body —
// the Skip is removed only in the task that makes it green. Each Given comment names the arrange the
// scenario needs. AC1-AC7 went green in T546 (SPEC F198.1/F198.2); AC8-AC9 stay skipped for T547
// (CrosstalkTimeline is out of T546's scope). ScenarioCommaRunAtAFragmentBoundary pins the fragment-boundary
// case (SPEC F197.2 + F198.1), beyond the original AC1-AC9 set.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheflattenkeepsthepause
{
    const string Pending = "pending: T547 — The flatten keeps the pause (STORY-465)";

    public sealed class ScenarioCopyWithCommas
    {
        // Given: "Tonight, on GenWave, the hits"

        /// <summary>AC1 — commas survive</summary>
        [Fact]
        public void KeepsTheCommas()
        {
            var spoken = SpeechText.FlattenForSpeech("Tonight, on GenWave, the hits");

            Assert.Equal("tonight, on genwave, the hits", spoken);
        }
    }

    public sealed class ScenarioLooseMarks
    {
        // Given: "Coming up: the news" · "Rock — and roll – tonight - late" · "Wait for it… now" · "Well, — well"

        /// <summary>AC2 — colon → comma</summary>
        [Fact]
        public void FoldsAColonToAComma()
        {
            var spoken = SpeechText.FlattenForSpeech("Coming up: the news");

            Assert.Equal("coming up, the news", spoken);
        }

        /// <summary>AC3 — every dash → comma</summary>
        [Fact]
        public void FoldsDashesToCommas()
        {
            var spoken = SpeechText.FlattenForSpeech("Rock — and roll – tonight - late");

            Assert.Equal("rock, and roll, tonight, late", spoken);
        }

        /// <summary>AC4 — ellipsis → comma</summary>
        [Fact]
        public void FoldsAnEllipsisToAComma()
        {
            var spoken = SpeechText.FlattenForSpeech("Wait for it… now");

            Assert.Equal("wait for it, now", spoken);
        }

        /// <summary>AC5 — a run → one comma</summary>
        [Fact]
        public void CollapsesARun()
        {
            var spoken = SpeechText.FlattenForSpeech("Well, — well");

            Assert.Equal("well, well", spoken);
        }
    }

    public sealed class ScenarioInWordHyphens
    {
        // Given: "re-render the drive-time show"

        /// <summary>AC6 — untouched</summary>
        [Fact]
        public void KeepsInWordHyphens()
        {
            var spoken = SpeechText.FlattenForSpeech("re-render the drive-time show");

            Assert.Equal("re-render the drive-time show", spoken);
        }
    }

    public sealed class ScenarioTheCacheEpoch
    {
        // Given: TtsSegmentSource.MergePolicyVersion

        /// <summary>AC7 — ends with "+gh703"</summary>
        [Fact]
        public void MovesTheEpoch()
        {
            Assert.EndsWith("+gh703", TtsSegmentSource.MergePolicyVersion, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioCommaRunAtAFragmentBoundary
    {
        // Fragment boundary (SPEC F197.2 + F198.1): the comma-run collapse (SPEC F198.1) used to decide "is
        // this the end of the segment?" by measuring against the FlattenProse call's own INPUT —
        // a fragment, not the whole string. Every phone-shaped match (SPEC F197.2) and every
        // [...] markup span carves the text into fragments, so that heuristic fired at every
        // fragment boundary, not just the true end of the string, and silently dropped the space
        // that was supposed to separate the comma from whatever came next.

        /// <summary>
        /// A comma immediately before a phone-shaped match (SPEC F197.2) sits at the end of the
        /// prose FRAGMENT that precedes it, not at the end of the string — the space after it must
        /// survive to separate it from the spoken digits.
        /// </summary>
        [Fact]
        public void KeepsTheSpaceBeforeASpokenPhoneNumber()
        {
            var spoken = SpeechText.FlattenForSpeech("Call us: 555-0142");

            Assert.Equal("call us, five five five, zero one four two", spoken);
        }

        /// <summary>
        /// A comma immediately before a <c>[...]</c> markup span sits at the end of the prose
        /// fragment FlattenSegment sees for that span, not at the end of the string — the space
        /// before the token must survive.
        /// </summary>
        [Fact]
        public void KeepsTheSpaceBeforeAMarkupToken()
        {
            var spoken = SpeechText.FlattenForSpeech("Coming up, [pause:1s] the news");

            Assert.Equal("coming up, [pause:1s] the news", spoken);
        }

        /// <summary>
        /// An ellipsis (SPEC F198.1) at the TRUE end of the string is the one case where there is
        /// nothing left to separate the comma from — the trailing space collapses away.
        /// </summary>
        [Fact]
        public void DropsTheTrailingSpaceAtTheTrueEndOfTheString()
        {
            var spoken = SpeechText.FlattenForSpeech("Wait for it...");

            Assert.Equal("wait for it,", spoken);
        }
    }

    public sealed class ScenarioThreeConsecutiveAnnouncerLines
    {
        // Given: a two-voice script through CrosstalkTimeline with a seeded sampler (T547)

        /// <summary>AC8 — same-speaker gaps within [0.20, 0.35] s</summary>
        [Fact(Skip = Pending)]
        public void ClampsSameSpeakerGaps() => Assert.Fail(Pending);

        /// <summary>AC9 — the ANNOUNCER→VOICE gap within [0.20, 0.80] s</summary>
        [Fact(Skip = Pending)]
        public void LeavesCrossSpeakerGapsAlone() => Assert.Fail(Pending);
    }

}
