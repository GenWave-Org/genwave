using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A dry-run arrangement: two engines (the second optionally gpu-only) on a counting stub, ten lines, a root that does not exist until
/// something is seeded into it, and a fake docker that counts its calls. <see cref="Run"/> drives <c>render --dry-run</c> through the CLI.
/// </summary>
internal sealed class DryRunFixture : IDisposable
{
    private readonly RenderScenario scenario = new();
    private readonly EngineStubServer stub;
    private readonly string engines;
    private readonly string gauntlet;
    private readonly FakeDockerInspector docker = new(new ContainerState(true, true, "10.0.0.2"));

    internal DryRunFixture(bool secondEngineGpuOnly = false)
    {
        stub = scenario.StartStubAsync().GetAwaiter().GetResult();
        scenario.AddEngine("alpha", stub.BaseUri);
        scenario.AddEngine("beta", stub.BaseUri, tiers: secondEngineGpuOnly ? ["gpu"] : null);
        engines = scenario.EnginesDirectory;
        gauntlet = scenario.GauntletDirectory(Enumerable.Range(1, 10).Select(i => GauntletFixtureDirectory.Line($"leadin-{i}", "LeadIn", RenderScenario.Eight)).ToArray());
    }

    internal string Root => scenario.Root;

    internal int StubRequests => stub.Requests.TotalRequests;

    internal int DockerCalls => docker.Asked.Count;

    /// <summary>Writes a finished run's <c>timing.jsonl</c> with one ok record per wall time (a negative wall time is a failed clip).</summary>
    internal void SeedRun(string runId, params double[] wallMs)
    {
        TimingLog log = new(new RunLayout(Root, runId).TimingPath);
        foreach (double wall in wallMs)
        {
            log.AppendAsync(new TimingRecord("alpha", "leadin-1", EngineTier.Cpu, Math.Abs(wall), 3000, wall >= 0, null), CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }

    /// <summary>Writes a <c>timing.jsonl</c> that is not timing records.</summary>
    internal void SeedGarbage(string runId)
    {
        string path = new RunLayout(Root, runId).TimingPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);
        File.WriteAllText(path, "not json\nstill not json\nnor this\n");
    }

    /// <summary>Everything under the root, relative and sorted: empty when the root does not exist.</summary>
    internal string[] Entries() => Directory.Exists(Root)
        ? Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Select(e => Path.GetRelativePath(Root, e)).Order(StringComparer.Ordinal).ToArray()
        : [];

    internal RenderRun Run(string tier = "cpu", params string[] extra) =>
        scenario.RunAt(Root, docker, CancellationToken.None, engines, gauntlet, tier, ["--dry-run", .. extra]);

    public void Dispose() => scenario.Dispose();
}
