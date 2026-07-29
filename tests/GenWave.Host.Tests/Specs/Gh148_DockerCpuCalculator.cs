// gh-#148 — the docker CPU-percentage formula's edges, pinned directly on DockerCpuCalculator.
//
// BDD specification — xUnit. The formula (see the calculator's own remarks):
//   cpu% = (Δcpu_total / Δsystem_total) × online_cpus × 100
// Sad paths all resolve to null — an honest "unknown", never a fabricated 0 — while a genuinely
// idle container (zero cpu delta over a positive system delta) is an honest 0.

using GenWave.Host.Stats;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDockerCpuCalculator
{
    static DockerCpuStats Sample(ulong total, ulong system, int? cpus = 4) =>
        new()
        {
            CpuUsage = new DockerCpuUsage { TotalUsage = total },
            SystemCpuUsage = system,
            OnlineCpus = cpus,
        };

    public sealed class ScenarioComputableSamples
    {
        [Fact]
        public void TwoHonestSamplesComputeThePerCoreScaledPercentage()
        {
            // Δcpu 300e6 over Δsystem 6e9 on 4 cpus ⇒ 20.0%
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 400_000_000, system: 16_000_000_000),
                Sample(total: 100_000_000, system: 10_000_000_000));

            Assert.NotNull(percent);
            Assert.Equal(20.0, percent.Value, precision: 6);
        }

        [Fact]
        public void RealCapturedSamplesReproduceDockerStatsFigure()
        {
            // Captured live from genwave-api-1 through the pinned proxy (112-cpu host):
            // Δcpu 5_258_000, Δsystem 112_210_000_000 ⇒ ≈ 0.5248%.
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 2_509_209_537_000, system: 58_816_516_060_000_000, cpus: 112),
                Sample(total: 2_509_204_279_000, system: 58_816_403_850_000_000, cpus: 112));

            Assert.NotNull(percent);
            Assert.Equal(0.5248, percent.Value, precision: 4);
        }

        [Fact]
        public void AnIdleContainerIsAnHonestZeroNotNull()
        {
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 100_000_000, system: 16_000_000_000),
                Sample(total: 100_000_000, system: 10_000_000_000));

            Assert.Equal(0.0, percent);
        }
    }

    public sealed class SadPathUncomputableSamples
    {
        [Fact]
        public void TheFirstSampleEdgeIsNull()
        {
            // docker zeroes precpu_stats when no previous sample exists (system_cpu_usage 0).
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 400_000_000, system: 16_000_000_000),
                Sample(total: 0, system: 0));

            Assert.Null(percent);
        }

        [Fact]
        public void AZeroSystemDeltaIsNull()
        {
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 400_000_000, system: 16_000_000_000),
                Sample(total: 100_000_000, system: 16_000_000_000));

            Assert.Null(percent);
        }

        [Fact]
        public void ABackwardsCpuCounterIsNull()
        {
            // A daemon restart can reset counters — unsigned subtraction would wrap huge.
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 100_000_000, system: 16_000_000_000),
                Sample(total: 400_000_000, system: 10_000_000_000));

            Assert.Null(percent);
        }

        [Fact]
        public void AnUnknownCpuCountIsNull()
        {
            var percent = DockerCpuCalculator.CpuPercent(
                Sample(total: 400_000_000, system: 16_000_000_000, cpus: null),
                Sample(total: 100_000_000, system: 10_000_000_000, cpus: null));

            Assert.Null(percent);
        }

        [Fact]
        public void MissingSamplesAreNull()
        {
            Assert.Null(DockerCpuCalculator.CpuPercent(null, Sample(1, 1)));
            Assert.Null(DockerCpuCalculator.CpuPercent(Sample(1, 1), null));
            Assert.Null(DockerCpuCalculator.CpuPercent(null, null));
        }
    }
}
