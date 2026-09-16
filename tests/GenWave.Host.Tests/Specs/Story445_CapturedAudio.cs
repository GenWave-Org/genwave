// STORY-445 — Captured audio proves no dead air and level (gh-#777 · SPEC F178.3, F178.5 · PLAN T489/T490/T491)
//
// BDD specification — xUnit. AC1 runs tools/gate/make_media.sh with the real ffmpeg and measures
// its two tracks back with ebur128; AC2 pins tools/gate/media/; AC3–AC6 drive
// tools/gate/measure_audio.sh over WAVs synthesised with ffmpeg lavfi; AC7–AC10 drive the capture
// leg of stack_gate.sh through GateHarness, where the fake station serves a real WAV on /stream
// and the real ffmpeg records CAPTURE_SECS of it.
//
// RED at plan time: tools/gate/ does not exist.

using System.Diagnostics;
using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;
using static GenWave.Host.Tests.Support.GateHarness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCapturedAudioProvesNoDeadAirAndLevel
{
    static string Repo => RepoRootLocator.Find(AppContext.BaseDirectory);
    static string MediaDir => Path.Combine(Repo, "tools", "gate", "media");

    static (int ExitCode, string StdOut, string StdErr) Measure(string wav, double target, IReadOnlyDictionary<string, string>? env = null) =>
        ScriptProcess.Run(Path.Combine(Repo, "tools", "gate", "measure_audio.sh"), RealPath, null, env,
            wav, "--target", target.ToString(System.Globalization.CultureInfo.InvariantCulture));

    static double IntegratedLufs(string file)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-nostats", "-hide_banner", "-i", file, "-filter_complex", "ebur128=peak=true", "-f", "null", "-" })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var m = Regex.Matches(err, @"I:\s*(-?[0-9.]+)\s*LUFS");
        return double.Parse(m[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the synthesised media
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSynthesisedTracksHitTheirTargets
    {
        readonly string outDir = Directory.CreateTempSubdirectory("story445-media-").FullName;
        readonly int exitCode;

        public ScenarioTheSynthesisedTracksHitTheirTargets() =>
            exitCode = ScriptProcess.Run(Path.Combine(Repo, "tools", "gate", "make_media.sh"), RealPath, null, null, outDir).ExitCode;

        [Fact]
        public void TheScriptSucceeds() => Assert.Equal(0, exitCode);

        [Fact]
        public void TheLoudTrackIsAtMinusTwelve() =>
            Assert.InRange(IntegratedLufs(Path.Combine(outDir, "tone-loud.mp3")), -13, -11);

        [Fact]
        public void TheQuietTrackIsAtMinusThirty() =>
            Assert.InRange(IntegratedLufs(Path.Combine(outDir, "tone-quiet.mp3")), -31, -29);
    }

    public sealed class ScenarioTheCommittedClipsAreSmallAndSourced
    {
        readonly string[] audio = Directory.Exists(MediaDir)
            ? Directory.EnumerateFiles(MediaDir).Where(f => !f.EndsWith(".md", StringComparison.Ordinal)).ToArray()
            : [];
        readonly string sources = File.Exists(Path.Combine(MediaDir, "SOURCES.md")) ? File.ReadAllText(Path.Combine(MediaDir, "SOURCES.md")) : "";

        [Fact]
        public void ThereAreClips() => Assert.NotEmpty(audio);

        [Fact]
        public void EveryClipIsAtMostOneMegabyte() =>
            Assert.Empty(audio.Where(f => new FileInfo(f).Length > 1024 * 1024).Select(Path.GetFileName));

        [Fact]
        public void EveryClipIsNamedInSources() =>
            Assert.DoesNotContain(audio.Select(Path.GetFileName), n => !sources.Contains(n!, StringComparison.Ordinal));

        [Fact]
        public void EveryClipHasAUrl() =>
            Assert.Equal(audio.Length, Regex.Matches(sources, @"https?://\S+").Count);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — measure_audio.sh
    // ---------------------------------------------------------------------

    public sealed class ScenarioContinuousAudioAtTargetPasses
    {
        readonly int exitCode = Measure(MakeWav(-14, seconds: 30), -14).ExitCode;

        [Fact]
        public void ExitIsZero() => Assert.Equal(0, exitCode);
    }

    public sealed class ScenarioTheToleranceIsAVariable
    {
        readonly int exitCode = Measure(MakeWav(-22, seconds: 10), -14, new Dictionary<string, string> { ["TOL_LU"] = "9" }).ExitCode;

        [Fact]
        public void ExitIsZeroUnderAWideTolerance() => Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the capture leg
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPassingCaptureReportsEveryMeasurement : IDisposable
    {
        readonly FakeStation station = new() { LoudnessTargetLufs = -16, StreamWav = MakeWav(-16, seconds: 6) };
        readonly Run run;

        public ScenarioAPassingCaptureReportsEveryMeasurement() =>
            run = Execute(station, args: ["--tag", "v9.9.9", "--fresh", "--capture"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void TheLegPasses() => Assert.Equal(0, run.ExitCode);

        [Fact]
        public void TheTargetComesFromTheStation() =>
            Assert.Equal(-16, run.ReportJson?.RootElement.GetProperty("capture").GetProperty("target_lufs").GetDouble());

        [Fact]
        public void SilenceEventsAreListed() => Assert.Contains("silence events: 0", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void IntegratedLoudnessIsListed() => Assert.Matches(@"integrated: -?\d+(\.\d+)? LUFS", run.ReportMd);

        [Fact]
        public void TheBoothLogCountIsListed() => Assert.Contains("booth_log: 3", run.ReportMd, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — measure_audio.sh
    // ---------------------------------------------------------------------

    public sealed class ScenarioASilenceGapFailsTheMeasure
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = Measure(MakeWav(-14, seconds: 10, withGap: true), -14);

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, result.ExitCode);

        [Fact]
        public void OneSilenceEventIsNamed() => Assert.Contains("silence_events=1", result.StdOut, StringComparison.Ordinal);
    }

    public sealed class ScenarioALevelMissFails
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = Measure(MakeWav(-22, seconds: 10), -14, new Dictionary<string, string> { ["TOL_LU"] = "5" });

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, result.ExitCode);

        [Fact]
        public void TheMeasuredLoudnessIsNamed() => Assert.Matches(@"integrated_lufs=-2[123](\.\d+)?", result.StdOut);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the capture leg
    // ---------------------------------------------------------------------

    public sealed class ScenarioCaptureWithoutFreshIsUsage : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioCaptureWithoutFreshIsUsage() => run = Execute(station, args: ["--tag", "v9.9.9", "--capture"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsTwo() => Assert.Equal(2, run.ExitCode);

        [Fact]
        public void StderrSaysCaptureRequiresFresh() => Assert.Contains("--capture requires --fresh", run.StdErr, StringComparison.Ordinal);
    }

    public sealed class ScenarioSpeechMustHaveAired : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6) };
        readonly Run run;

        public ScenarioSpeechMustHaveAired() =>
            run = Execute(station, env: new Dictionary<string, string> { ["GATE_STUB_BOOTH_COUNT"] = "0" }, args: ["--tag", "v9.9.9", "--fresh", "--capture"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void BoothLogIsTheFailingAssertion() => Assert.Equal("booth_log", run.FirstFailure);
    }

    public sealed class ScenarioAnOffLevelCaptureFails : IDisposable
    {
        readonly FakeStation station = new() { LoudnessTargetLufs = -16, StreamWav = MakeWav(-30, seconds: 6) };
        readonly Run run;

        public ScenarioAnOffLevelCaptureFails() =>
            run = Execute(station, env: new Dictionary<string, string> { ["CAPTURE_SECS"] = "10" },
                args: ["--tag", "v9.9.9", "--fresh", "--capture"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void LoudnessIsTheFailingAssertion() => Assert.Equal("loudness", run.FirstFailure);

        [Fact]
        public void TheMeasuredLoudnessIsStillListed() => Assert.Matches(@"integrated: -?\d+(\.\d+)? LUFS", run.ReportMd);
    }

    public sealed class ScenarioASilentGapInTheCaptureFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-16, seconds: 6, withGap: true) };
        readonly Run run;

        public ScenarioASilentGapInTheCaptureFails() =>
            run = Execute(station, env: new Dictionary<string, string> { ["CAPTURE_SECS"] = "10" },
                args: ["--tag", "v9.9.9", "--fresh", "--capture"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void SilenceIsTheFailingAssertion() => Assert.Equal("silence", run.FirstFailure);

        [Fact]
        public void TheEventCountIsListed() => Assert.Contains("silence events: 1", run.ReportMd, StringComparison.Ordinal);
    }
}
