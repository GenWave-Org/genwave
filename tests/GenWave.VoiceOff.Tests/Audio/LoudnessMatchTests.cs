using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Audio;

public sealed class LoudnessMatchTests
{
    public sealed class AnInputFfmpegCannotRead : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.ZeroedHeaderAsync("junk.wav");

        [Fact]
        public void TheResultIsOkFalse() => Assert.False(Result.Ok);

        [Fact]
        public void TheReasonNamesThePassOneRead() => Assert.StartsWith("ffmpeg could not read the clip (exit ", Result.Error, StringComparison.Ordinal);

        [Fact]
        public void NoFileIsLeft() => Assert.Empty(FilesNamedMatched());
    }

    public sealed class AFailedMatchOverAStaleOutput : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.ZeroedHeaderAsync("junk.wav");

        protected override Task SeedAsync() => File.WriteAllTextAsync(OutputPath, "stale clip from an earlier run");

        [Fact]
        public void TheStaleOutputIsDeleted() => Assert.False(File.Exists(OutputPath));
    }

    public sealed class APassTwoFfmpegFailure : MatchScenario
    {
        private protected override string OutputPath => Clips.PathFor(Path.Combine("missing-folder", "matched.wav"));

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheResultIsOkFalse() => Assert.False(Result.Ok);

        [Fact]
        public void TheReasonNamesThePassTwoWrite() => Assert.StartsWith("ffmpeg could not write the matched clip (exit ", Result.Error, StringComparison.Ordinal);
    }

    public sealed class AMatchThatVerifiesOffTarget : MatchScenario
    {
        public AMatchThatVerifiesOffTarget() : base(new FixedLoudnessAnalyzer(-20.0))
        {
        }

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheResultIsOkFalse() => Assert.False(Result.Ok);

        [Fact]
        public void TheReasonCarriesTheMeasuredLevel() => Assert.Contains("-20.0", Result.Error, StringComparison.Ordinal);

        [Fact]
        public void TheMeasuredLevelIsReported() => Assert.Equal(-20.0, Result.MeasuredLufs);

        [Fact]
        public void NoFileAndNoTempIsLeft() => Assert.Empty(FilesNamedMatched());
    }

    public sealed class AMatchThatVerifiesAtTheEdgeOfTolerance : MatchScenario
    {
        public AMatchThatVerifiesAtTheEdgeOfTolerance() : base(new FixedLoudnessAnalyzer(-17.0))
        {
        }

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheResultIsOk() => Assert.True(Result.Ok);
    }

    public sealed class AnUnmeasurableVerify : MatchScenario
    {
        // A valid level with only the measurable flag false: the Measurable check alone must fail the match.
        public AnUnmeasurableVerify() : base(new FixedLoudnessAnalyzer(-16.0, measurable: false))
        {
        }

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheResultIsOkFalse() => Assert.False(Result.Ok);

        [Fact]
        public void TheReasonSaysItCouldNotBeMeasured() => Assert.Equal("matched clip could not be measured", Result.Error);

        [Fact]
        public void NoFileIsLeft() => Assert.Empty(FilesNamedMatched());
    }

    public sealed class APassingMatch : MatchScenario
    {
        public APassingMatch() : base(new FixedLoudnessAnalyzer(-16.4))
        {
        }

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheVerifiedLevelIsReported() => Assert.Equal(-16.4, Result.MeasuredLufs);
    }

    public sealed class AnExistingOutputOnSuccess : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        protected override Task SeedAsync() => File.WriteAllTextAsync(OutputPath, "old");

        [Fact]
        public void TheResultIsOk() => Assert.True(Result.Ok);

        [Fact]
        public void TheOutputIsReplaced() => Assert.True(new FileInfo(OutputPath).Length > 1000);
    }

    // The constant-level sine has no loudness range (LRA 0), which makes loudnorm fall back to dynamic mode.
    public sealed class AConstantLevelSine : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void LoudnormFallsBackToDynamic() => Assert.False(Result.Linear);

        [Fact]
        public void TheClipStillMatches() => Assert.True(Result.Ok);
    }

    public sealed class ASteppedSine : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.SteppedSineAsync();

        [Fact]
        public void LoudnormAppliesALinearGain() => Assert.True(Result.Linear);

        [Fact]
        public void TheClipMatches() => Assert.True(Result.Ok);
    }

    public sealed class AVerifyMissThatTheTrimCloses : MatchScenario
    {
        public AVerifyMissThatTheTrimCloses() : base(new SequencedLoudnessAnalyzer(-17.4, -16.1))
        {
        }

        protected override Task<string> InputAsync() => Clips.QuietSineAsync();

        [Fact]
        public void TheResultIsOk() => Assert.True(Result.Ok);

        [Fact]
        public void TheReVerifiedLevelIsReported() => Assert.Equal(-16.1, Result.MeasuredLufs);

        [Fact]
        public void NoTrimFileIsLeft() => Assert.Single(FilesNamedMatched());
    }

    [Fact]
    public void TheTrimFilterGainsAndLimitsAtTheTruePeakCeiling() =>
        Assert.Equal(
            "aformat=sample_rates=24000:channel_layouts=mono,volume=1.40dB,alimiter=limit=0.8414:level=false:latency=true",
            LoudnessMatch.TrimFilter(1.4));

    public sealed class APeakyClip : MatchScenario
    {
        protected override Task<string> InputAsync() => Clips.PeakySineAsync();

        [Fact]
        public void TheLimiterLetsItMatch() => Assert.True(Result.Ok);
    }

    [Fact]
    public void TheMeasureFilterTargetsMinus16WithTheStationCeilings() =>
        Assert.Equal("aformat=sample_rates=24000:channel_layouts=mono,loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", LoudnessMatch.MeasureFilter());

    [Fact]
    public void TheApplyFilterIsLinearAndEchoesTheMeasurement() =>
        Assert.Equal(
            "aformat=sample_rates=24000:channel_layouts=mono,volume=13.90dB,alimiter=limit=0.7079:level=false:latency=true,loudnorm=I=-16:TP=-1.5:LRA=11:measured_I=-30.10:measured_TP=-8.20:measured_LRA=0.00:measured_thresh=-40.60:offset=0.30:linear=true:print_format=json",
            LoudnessMatch.ApplyFilter(new LoudnormMeasurement("-30.10", "-8.20", "0.00", "-40.60", "0.30"), 13.9));

    [Fact]
    public void AnUnlimitedApplyFilterIsTheFormatAndLoudnormAlone() =>
        Assert.Equal(
            "aformat=sample_rates=24000:channel_layouts=mono,loudnorm=I=-16:TP=-1.5:LRA=11:measured_I=-30.10:measured_TP=-8.20:measured_LRA=0.00:measured_thresh=-40.60:offset=0.30:linear=true:print_format=json",
            LoudnessMatch.ApplyFilter(new LoudnormMeasurement("-30.10", "-8.20", "0.00", "-40.60", "0.30")));

    [Fact]
    public void TheLimitedMeasureFilterPutsTheGainAndLimiterAheadOfLoudnorm() =>
        Assert.Equal(
            "aformat=sample_rates=24000:channel_layouts=mono,volume=-2.50dB,alimiter=limit=0.7079:level=false:latency=true,loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json",
            LoudnessMatch.MeasureFilter(-2.5));

    public sealed class ThePassTwoArgv
    {
        private readonly string joined = string.Join(' ', ClipScrubber.BuildArgs("in.wav", "out.wav", "loudnorm=linear=true"));

        [Fact]
        public void ItCarriesTheFilter() => Assert.Contains("-af loudnorm=linear=true", joined, StringComparison.Ordinal);

        [Fact]
        public void ItFixes24KHz() => Assert.Contains("-ar 24000", joined, StringComparison.Ordinal);

        [Fact]
        public void ItFixesMono() => Assert.Contains("-ac 1", joined, StringComparison.Ordinal);

        [Fact]
        public void ItWritesPcmS16le() => Assert.Contains("-c:a pcm_s16le", joined, StringComparison.Ordinal);

        [Fact]
        public void ItDropsMetadata() => Assert.Contains("-map_metadata -1", joined, StringComparison.Ordinal);

        [Fact]
        public void ItIsBitexact() => Assert.Contains("-fflags +bitexact", joined, StringComparison.Ordinal);
    }

    public sealed class ThePathsOnTheArgv
    {
        private readonly IReadOnlyList<string> args = ClipScrubber.BuildArgs("-evil.wav", "-out.wav");

        [Fact]
        public void TheInputIsAFileUrlWithAFullPath() =>
            Assert.Equal("file:" + Path.GetFullPath("-evil.wav"), args[args.ToList().IndexOf("-i") + 1]);

        [Fact]
        public void TheOutputIsAFullPath() => Assert.Equal(Path.GetFullPath("-out.wav"), args[^1]);
    }

    [Fact]
    public void ThePassOneInputIsAFileUrlWithAFullPath()
    {
        IReadOnlyList<string> args = ClipScrubber.MeasureArgs("-evil.wav", "loudnorm");
        Assert.Equal("file:" + Path.GetFullPath("-evil.wav"), args[args.ToList().IndexOf("-i") + 1]);
    }

    [Fact]
    public void ThePassTwoFilterAsksForTheJsonReport() =>
        Assert.EndsWith("print_format=json", LoudnessMatch.ApplyFilter(new LoudnormMeasurement("-30", "-8", "1", "-40", "0")), StringComparison.Ordinal);

    [Theory]
    [InlineData("""{ "input_i" : "-inf", "input_tp" : "-inf", "input_lra" : "0.00", "input_thresh" : "-70.00", "target_offset" : "inf" }""")]
    [InlineData("no json here")]
    [InlineData("{ not json }")]
    public void AnUnmeasurableReportParsesToNull(string stderr) => Assert.Null(LoudnormMeasurement.TryParse("log line\n" + stderr));

    [Fact]
    public void AFiniteReportParsesFromTheTrailingBlock() =>
        Assert.Equal(
            new LoudnormMeasurement("-30.10", "-8.20", "0.00", "-40.60", "0.30"),
            LoudnormMeasurement.TryParse("""
                [Parsed_loudnorm_0 @ 0x1]
                {
                	"input_i" : "-30.10",
                	"input_tp" : "-8.20",
                	"input_lra" : "0.00",
                	"input_thresh" : "-40.60",
                	"target_offset" : "0.30"
                }
                """));

    [Theory]
    [InlineData("linear", true)]
    [InlineData("dynamic", false)]
    public void ThePassTwoReportNamesTheMode(string mode, bool linear) =>
        Assert.Equal(linear, LoudnormMeasurement.IsLinear($$"""
            [Parsed_loudnorm_0 @ 0x1]
            {
            	"output_i" : "-15.97",
            	"normalization_type" : "{{mode}}",
            	"target_offset" : "0.03"
            }
            """));

    [Fact]
    public void AReportWithNoModeIsNotLinear() => Assert.False(LoudnormMeasurement.IsLinear("no json"));
}
