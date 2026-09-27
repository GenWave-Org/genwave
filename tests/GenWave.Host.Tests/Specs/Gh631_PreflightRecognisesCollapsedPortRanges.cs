using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

// gh-#631: docker collapses adjacent published ports into a range, so the api's 8080+8081 reads
// "0.0.0.0:8080-8081->8080-8081/tcp" in `docker ps --format '{{.Ports}}'`. The ownership check
// must see both ports as this stack's own (a relaunch PASS) and still refuse a port outside it.
public static class FeaturePreflightRecognisesCollapsedPortRanges
{
    const string CollapsedRangeBlob =
        "0.0.0.0:8000->8000/tcp, :::8000->8000/tcp\n" +
        "0.0.0.0:8080-8081->8080-8081/tcp, [::]:8080-8081->8080-8081/tcp";

    static bool IsDockerOwned(int port, string blob)
    {
        var scriptPath = Path.Combine(TempDir.CreateForProcessLifetime(), "run.sh");
        File.WriteAllText(scriptPath,
            "set -euo pipefail; . tools/preflight.sh; preflight_port_is_docker_owned \"$1\" \"$2\"");

        var (exitCode, _, _) = ScriptProcess.Run(
            scriptPath, ScriptProcess.MakeBinDir(), args: [port.ToString(), blob]);
        return exitCode == 0;
    }

    public sealed class ScenarioCollapsedRange
    {
        [Fact]
        public void AnExactlyPublishedPortIsOwned() => Assert.True(IsDockerOwned(8000, CollapsedRangeBlob));

        [Fact]
        public void TheRangeStartIsOwned() => Assert.True(IsDockerOwned(8080, CollapsedRangeBlob));

        [Fact]
        public void APortInsideTheRangeIsOwned() => Assert.True(IsDockerOwned(8081, CollapsedRangeBlob));

        [Fact]
        public void APortAfterTheRangeIsNotOwned() => Assert.False(IsDockerOwned(8082, CollapsedRangeBlob));

        [Fact]
        public void APortBeforeTheRangeIsNotOwned() => Assert.False(IsDockerOwned(8079, CollapsedRangeBlob));

        [Fact]
        public void AContainerSidePortIsNeverMistakenForAHostPort() =>
            Assert.False(IsDockerOwned(3000, "0.0.0.0:13000->3000/tcp"));

        [Fact]
        public void AnEmptyBlobOwnsNothing() => Assert.False(IsDockerOwned(8080, ""));
    }
}
