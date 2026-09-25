// STORY-464 — Phone numbers are spoken as digits (gh-#700 · SPEC F197 · PLAN T545)
//
// BDD specification — xUnit. GREEN as of T545: SpeechText.FlattenSegment now matches a
// phone-shaped run (GenWave.Core.PhoneShape.Regex) before the ordinary prose flatten can strip its
// separators, and replaces it outright with its digits spoken one by one.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePhonenumbersarespokenasdigits
{
    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    public sealed class ScenarioASevenDigitNumber
    {
        /// <summary>AC3 — "call five five five, zero one four two today"</summary>
        [Fact]
        public void SpeaksEachDigit()
        {
            var spoken = SpeechText.FlattenForSpeech("Call 555-0142 today");

            Assert.Equal("call five five five, zero one four two today", spoken);
        }
    }

    public sealed class ScenarioATenDigitNumber
    {
        /// <summary>AC4 — three comma-separated groups</summary>
        [Fact]
        public void SpeaksThreeGroups()
        {
            var spoken = SpeechText.FlattenForSpeech("Call 812-555-0199");

            Assert.Equal("call eight one two, five five five, zero one nine nine", spoken);
        }
    }

    public sealed class ScenarioAParenthesizedExchangeNumber
    {
        // Given: GenWave.Core.PhoneShape's gh-#856 alternative — "(555) 0142" and its dash/dot
        // siblings — all split into the SAME two digit groups ("555"/"0142") regardless of which
        // separator sits between the closing paren and the final four digits, so every variant
        // speaks identically to the plain "555-0142" form (AC3 above).

        /// <summary>gh-#856 — space separator</summary>
        [Fact]
        public void SpeaksEachDigitWithSpaceSeparator()
        {
            var spoken = SpeechText.FlattenForSpeech("Call (555) 0142 today");

            Assert.Equal("call five five five, zero one four two today", spoken);
        }

        /// <summary>gh-#856 — dash separator</summary>
        [Fact]
        public void SpeaksEachDigitWithDashSeparator()
        {
            var spoken = SpeechText.FlattenForSpeech("Call (555)-0142 today");

            Assert.Equal("call five five five, zero one four two today", spoken);
        }

        /// <summary>gh-#856 — dot separator</summary>
        [Fact]
        public void SpeaksEachDigitWithDotSeparator()
        {
            var spoken = SpeechText.FlattenForSpeech("Call (555).0142 today");

            Assert.Equal("call five five five, zero one four two today", spoken);
        }

        /// <summary>gh-#856 — no separator at all</summary>
        [Fact]
        public void SpeaksEachDigitWithNoSeparator()
        {
            var spoken = SpeechText.FlattenForSpeech("Call (555)0142 today");

            Assert.Equal("call five five five, zero one four two today", spoken);
        }
    }

    public sealed class ScenarioADjPatterRender : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer innerSynth = new();

        /// <summary>
        /// AC5 — every voice: a DJ patter render request carrying "555-0142" reaches the
        /// synthesizer in its spoken-digit form. Wires <see cref="TtsSegmentSource"/> through the
        /// REAL <see cref="NormalizingTtsSynthesizer"/> (the one production hand-off
        /// SpeechText.Normalize applies through — see Story005's
        /// ScenarioCorrectionsRebuildReKeysTheCache for the identical arrange) over a recording
        /// <see cref="FakeTtsSynthesizer"/>, so this proves the flatten reaches every voice's
        /// render, not just the pure-function seam AC3/AC4 exercise directly.
        /// </summary>
        [Fact]
        public async Task ReachesEveryVoice()
        {
            var normalizingSynth = new NormalizingTtsSynthesizer(
                innerSynth, NoCorrections.Provider(), NoCorrections.PersonaCache(),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Call 555-0142 today."),
                normalizingSynth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), NoCorrections.Provider(),
                NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(), opts,
                NullLogger<TtsSegmentSource>.Instance);

            await source.RenderAsync(StationIdRequest(), CancellationToken.None);

            Assert.Contains("five five five, zero one four two", innerSynth.LastText, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(innerSynth.OutputDirectory)) Directory.Delete(innerSynth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNumbersThatAreNotPhones
    {
        /// <summary>AC6 — years unchanged: a 4-digit year is never phone-shaped.</summary>
        [Fact]
        public void LeavesYearsAlone()
        {
            var spoken = SpeechText.FlattenForSpeech("Back in 1994 and 2026");

            Assert.Equal("back in 1994 and 2026", spoken);
        }

        /// <summary>
        /// AC7 — a time is not a phone: the digit pass never touches "7:30" itself, since it is
        /// too short for PhoneShape.Regex's 3-3-4/3-4/7+ shapes. The intra-digit colon survives
        /// because SPEC F198.1's LooseMarkRx (gh-#703, PLAN T546) treats a mark with a digit on
        /// BOTH sides as an intra-word survivor, same as any other identity mark.
        /// </summary>
        [Fact]
        public void LeavesTimesAlone()
        {
            var spoken = SpeechText.FlattenForSpeech("at 7:30 tonight");

            Assert.Equal("at 7:30 tonight", spoken);
        }

        /// <summary>AC8 — counts unchanged: "1000" is 4 digits, short of the 7-digit bare-run floor.</summary>
        [Fact]
        public void LeavesCountsAlone()
        {
            var spoken = SpeechText.FlattenForSpeech("over 1000 tracks");

            Assert.Equal("over 1000 tracks", spoken);
        }
    }
}
