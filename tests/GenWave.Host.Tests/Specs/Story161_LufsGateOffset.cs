// STORY-161 — The recorded-LUFS gate is meaningful at any target (Epic Z / SPEC F64,
// closes gitea-#204).
//
// BDD specification — xUnit. Story013's opt-in recorded-stream gate asserts measured ≈
// (effective TargetLufs − ProgramLoudnessOffset) ± 2.5; the named constant was seeded ≈3.5 LU
// from the 2026-07-12 live reading and has now been RE-DERIVED to 1.5 LU at the Z11 scratch-stack
// gate (operator ruling, 2026-07-15 — derivation evidence comes from that gate; run 2026-07-16) —
// the full derivation (target, measured mean, date, recording length, content notes) lands in the
// Story013 header. These specs pin the band MATH unit-level, and pin that the header carries the
// derivation contract, so both stay honest without spinning up the live stack the gate itself
// requires.

namespace GenWave.Host.Tests.Specs;

public static class FeatureLufsGateOffset
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Repo root, resolved relative to the test assembly's build output — the Story074/Story102/
    /// Story107/Story151/Story160 RepoRoot convention for reaching repo-root files from a test
    /// project.
    /// </summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Story013Text => File.ReadAllText(Path.Combine(
        RepoRoot, "tests", "GenWave.Host.Tests", "Specs",
        "Story013_AcceptanceGate02_LevelMatchingRealKokoro.cs"));

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the band models the offset
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioTheAssertionModelsTheOffset
    {
        [Fact]
        public void TheBandCentersOnTargetMinusTheOffset()
        {
            const double target = -16.0;
            const double offset = 1.5;
            const double tolerance = 2.5;

            var (lower, upper) = FeatureAcceptanceGate02LevelMatchingRealKokoro.ComputeExpectedLufsBand(
                target, offset, tolerance);

            var center = (lower + upper) / 2;
            Assert.Equal(target - offset, center, precision: 10);
            Assert.Equal(tolerance, upper - center, precision: 10);
            Assert.Equal(tolerance, center - lower, precision: 10);
        }

        [Fact]
        public void TheMinusTwelveOverrideBandContainsTheKnownStationMean()
        {
            // The gitea-#204 defect: at the operator's live TargetLufs=-12 override, the OLD band
            // (target ± 2.5 = [-14.5, -9.5]) structurally excluded the station's true long-window
            // mean (~-15.5, the 2026-07-12 diagnosis in docs/MEMORY.md). The offset-aware band
            // must contain it.
            const double operatorOverrideTarget = -12.0;
            const double knownStationMean = -15.5;

            var (lower, upper) = FeatureAcceptanceGate02LevelMatchingRealKokoro.ComputeExpectedLufsBand(
                operatorOverrideTarget,
                FeatureAcceptanceGate02LevelMatchingRealKokoro.ScenarioOutputStreamSitsAtTargetAcrossMixedSequence.ProgramLoudnessOffset,
                FeatureAcceptanceGate02LevelMatchingRealKokoro.ScenarioOutputStreamSitsAtTargetAcrossMixedSequence.Tolerance);

            Assert.InRange(knownStationMean, lower, upper);
        }

        [Fact]
        public void TheOffsetConstantIsDocumentedWithItsDerivation()
        {
            // A repo-content fact (the Story107/Story151/Story160 idiom): reads the Story013 file
            // itself and asserts its header carries the SPEC F64.2 derivation contract — target,
            // measured mean, date, and recording length for the readings behind the constant, plus
            // a citation of the Z11 re-derivation that settles it.
            var text = Story013Text;

            Assert.Contains("DERIVATION CONTRACT", text, StringComparison.Ordinal);

            // Reading A — the seeded value's source: target, measured mean, date, recording length.
            Assert.Contains("target -12", text, StringComparison.Ordinal);
            Assert.Contains("-15.5", text, StringComparison.Ordinal);
            Assert.Contains("2026-07-12", text, StringComparison.Ordinal);
            Assert.Contains("180s recording", text, StringComparison.Ordinal);

            // Reading B — the older -16-era reading, documented honestly even though it disagrees.
            Assert.Contains("target -16", text, StringComparison.Ordinal);
            Assert.Contains("-15.4", text, StringComparison.Ordinal);

            // Reading C — the Z11 scratch-stack re-derivation that this constant now carries.
            Assert.Contains("Reading C", text, StringComparison.Ordinal);
            Assert.Contains("-17.5", text, StringComparison.Ordinal);
            Assert.Contains("2026-07-16", text, StringComparison.Ordinal);

            // The re-derived constant and the citation of Z11's own gate.
            Assert.Contains("internal const double ProgramLoudnessOffset = 1.5;", text, StringComparison.Ordinal);
            Assert.Contains("Z11", text, StringComparison.Ordinal);
            Assert.Contains("STORY-162", text, StringComparison.Ordinal);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioDevBuildsNeverBlock
    {
        [Fact]
        public void TheGateSkipsWhenTheEnvVarIsUnset()
        {
            // Pins the mechanism Story013 ships today (F64.3): the recorded-stream gate treats
            // GENWAVE_LIVE_LUFS_GATE != "1" as opt-out and returns before touching the network.
            // A repo-content fact (the Story107/Story160 idiom) on the literal guard, rather than
            // invoking Story013's own gate fact here — that fact mutates the SAME process-wide
            // environment variable it reads, and xUnit runs test collections in parallel by
            // default, so calling it from a second collection would race Story013's own execution
            // of it. Reading the shipped guard verbatim pins the mechanism without that hazard.
            var text = Story013Text;

            Assert.Contains(
                "if (Environment.GetEnvironmentVariable(\"GENWAVE_LIVE_LUFS_GATE\") != \"1\")",
                text, StringComparison.Ordinal);
        }
    }
}
