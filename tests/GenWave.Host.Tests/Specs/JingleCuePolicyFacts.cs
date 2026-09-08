// SPEC F165.3 — a jingle's own role decides whether install stores the analyzer's own cue points or
// discards them for the file's full length (PLAN T414). Pure unit facts: no DB, no HTTP, no disk.

using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

public static class FeatureJingleCuePolicyDecidesTheStoredCueByRole
{
    static readonly CuePoints Analyzed = new(1.5, 4.2);
    const double DurationSec = 5.0;

    public sealed class ScenarioABedStoresItsFullLengthNeverTheAnalyzedTrim
    {
        [Fact]
        public void TheStoredCueSpansTheWholeFileNotTheSilenceTrimmedWindow()
        {
            var cue = JingleCuePolicy.Apply("bed", Analyzed, DurationSec);
            Assert.Equal(new CuePoints(0, DurationSec), cue);
        }
    }

    public sealed class ScenarioAStingStoresItsFullLengthNeverTheAnalyzedTrim
    {
        [Fact]
        public void TheStoredCueSpansTheWholeFileNotTheSilenceTrimmedWindow()
        {
            var cue = JingleCuePolicy.Apply("sting", Analyzed, DurationSec);
            Assert.Equal(new CuePoints(0, DurationSec), cue);
        }
    }

    public sealed class ScenarioAStationIdKeepsTheAnalyzersOwnCuePointsUnmodified
    {
        [Fact]
        public void TheStoredCueIsExactlyWhatTheAnalyzerProduced()
        {
            var cue = JingleCuePolicy.Apply("station_id", Analyzed, DurationSec);
            Assert.Equal(Analyzed, cue);
        }
    }

    // T414 smoke round 4 finding — ICueAnalyzer returns null by CONTRACT when no silence is detected
    // ("full-file playback is intended"), not as a refusal. Every role — including station_id, the
    // one role that otherwise keeps the analyzer's own cue points unmodified — must fall back to the
    // file's own full span rather than the caller (JinglePackController) ever treating this as an
    // enrichment failure.
    public sealed class ScenarioANullAnalysisFallsBackToTheFullSpanForEveryRole
    {
        [Theory]
        [InlineData("bed")]
        [InlineData("sting")]
        [InlineData("station_id")]
        public void TheStoredCueIsTheFilesOwnFullSpan(string role)
        {
            var cue = JingleCuePolicy.Apply(role, null, DurationSec);
            Assert.Equal(new CuePoints(0, DurationSec), cue);
        }
    }
}
