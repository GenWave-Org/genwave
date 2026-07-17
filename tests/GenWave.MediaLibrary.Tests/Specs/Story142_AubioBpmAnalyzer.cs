// STORY-142 — BPM measured at enrichment (Epic X / SPEC F46.1, closes gitea-#190) — aubio parse half.
// The Core contract lives in Core.Tests/Specs/Story142_BpmAnalyzerContract.cs; the
// enrichment/backfill half in Specs/Story142_BpmEnrichmentAndBackfill.cs (this project).
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-14, house rule since Epic S).
// House rule F46.5: these facts parse CAPTURED aubio output fixtures — the real binary is never
// invoked in tests (CI runners don't carry it); the real ffmpeg→aubio chain is X10 gate territory.
//
// Fixtures below match the REAL Debian aubio-tools output shape, verified in-container against the
// api image 2026-07-14: `aubio tempo /tmp/body.wav` → a single summary line, "94.49 bpm", exit 0 — not
// the per-beat-timestamp stream an earlier draft of this parser assumed (that variant is `aubio beat`).
//
// AubioBpmAnalyzer factors its argument-construction and output-parsing into pure `internal` static
// functions (BuildDecodeArguments / ParseTempo / InterpretAubioResult) — this project sees them via
// InternalsVisibleTo (Loudness.csproj). Every fact below drives one of those functions directly; none
// starts a process.

using GenWave.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAubioBpmAnalyzer
{
    public sealed class ScenarioACleanTempoEstimateLands
    {
        [Fact]
        public void ACleanAubioOutputYieldsItsBpmEstimateRoundedToOneDecimal()
        {
            // Captured-shape fixture: the real aubio tempo summary line, verbatim.
            const string fixture = "94.49 bpm\n";

            var bpm = AubioBpmAnalyzer.ParseTempo(fixture);

            // Math.Round(94.49, 1, AwayFromZero) = 94.5.
            Assert.Equal(94.5, bpm);
        }

        [Fact]
        public void ALeadingWarningLineIsIgnoredAndTheBpmLineIsParsed()
        {
            // Defensive case: some aubio builds/configs emit a warning line before the estimate.
            // The parser takes the bpm line, not the first line.
            const string fixture = """
                Warning: samplerate mismatch, resampling
                94.49 bpm
                """;

            var bpm = AubioBpmAnalyzer.ParseTempo(fixture);

            Assert.Equal(94.5, bpm);
        }

        [Fact]
        public void TheDecodeWindowIsTheCueTrimmedBody()
        {
            var args = AubioBpmAnalyzer.BuildDecodeArguments(
                path: "/media/track.flac",
                cueInSec: 5.0,
                cueOutSec: 95.0,
                outputPath: "/tmp/genwave-bpm-test.wav");

            var filterIndex = args.ToList().IndexOf("-af") + 1;
            Assert.True(filterIndex > 0);
            Assert.Equal("atrim=start=5:end=95,asetpts=PTS-STARTPTS", args[filterIndex]);
        }

        [Fact]
        public void ANullCueWindowDecodesTheWholeFile()
        {
            var args = AubioBpmAnalyzer.BuildDecodeArguments(
                path: "/media/track.flac",
                cueInSec: null,
                cueOutSec: null,
                outputPath: "/tmp/genwave-bpm-test.wav");

            var filterIndex = args.ToList().IndexOf("-af") + 1;
            Assert.True(filterIndex > 0);
            // Unbounded atrim (no "end=") — the whole file, start to EOF.
            Assert.Equal("atrim=start=0,asetpts=PTS-STARTPTS", args[filterIndex]);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioIndeterminateInputNeverThrows
    {
        [Fact]
        public void EmptyOutputYieldsNull()
        {
            Assert.Null(AubioBpmAnalyzer.ParseTempo(string.Empty));
        }

        [Fact]
        public void GarbageOutputYieldsNull()
        {
            const string garbage = """
                usage: aubio tempo [options]
                error: could not read file
                """;

            Assert.Null(AubioBpmAnalyzer.ParseTempo(garbage));
        }

        [Fact]
        public void ANonzeroExitCodeYieldsNull()
        {
            // Even when stdout carries what would otherwise be a clean, parseable estimate, a
            // non-zero aubio exit code short-circuits to null — the process failed regardless of
            // any partial output it wrote before failing.
            const string wouldOtherwiseParse = "94.49 bpm\n";

            Assert.Null(AubioBpmAnalyzer.InterpretAubioResult(exitCode: 1, stdout: wouldOtherwiseParse));
        }
    }
}
