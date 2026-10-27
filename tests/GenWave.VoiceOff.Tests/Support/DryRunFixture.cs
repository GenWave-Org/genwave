using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A dry-run arrangement: two cards on a counting stub (the second optionally refused by the words law), the repo's script (eight paragraphs),
/// a root that does not exist until something is seeded into it, and a fake docker that counts its calls. <see cref="Run"/> drives <c>render --dry-run</c> through the CLI.
/// </summary>
internal sealed class DryRunFixture : IDisposable
{
    private readonly RenderScenario scenario = new();
    private readonly EngineStubServer stub;
    private int seeded;
    private readonly FakeDockerInspector docker = new(new ContainerState(true, true, "10.0.0.2"));

    internal DryRunFixture(bool secondCardBreaksTheWordsLaw = false)
    {
        stub = scenario.StartStubAsync().GetAwaiter().GetResult();
        scenario.AddEngine("alpha", stub.BaseUri);
        scenario.AddCard("card-one", "alpha");
        scenario.AddCard("card-two", "alpha", secondCardBreaksTheWordsLaw ? n => n["overrides"] = new System.Text.Json.Nodes.JsonObject { ["3"] = "Other words entirely." } : null);
    }

    internal string Root => scenario.Root;

    internal int StubRequests => stub.Requests.TotalRequests;

    internal int DockerCalls => docker.Asked.Count;

    /// <summary>Writes a finished run's <c>renders.jsonl</c> with one record per wall time (a negative wall time is a failed paragraph).</summary>
    internal void SeedRun(string runId, params double[] wallMs)
    {
        seeded++;
        string runLogPath = new RunLayout(Root, runId).RendersPath;
        RenderLog log = new(runLogPath);
        foreach (double wall in wallMs)
        {
            log.AppendAsync(new RenderRecord("card-one", 1, 40, Math.Abs(wall), 3000, wall >= 0, wall >= 0 ? null : "boom"), CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        // Later seeds are newer, whatever the filesystem's timestamp resolution.
        File.SetLastWriteTimeUtc(runLogPath, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc).AddMinutes(seeded));
    }

    /// <summary>Writes a <c>renders.jsonl</c> that is not render records.</summary>
    internal void SeedGarbage(string runId)
    {
        string path = new RunLayout(Root, runId).RendersPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);
        File.WriteAllText(path, "not json\nstill not json\nnor this\n");
    }

    /// <summary>Everything under the root, relative and sorted: empty when the root does not exist.</summary>
    internal string[] Entries() => Directory.Exists(Root)
        ? Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Select(e => Path.GetRelativePath(Root, e)).Order(StringComparer.Ordinal).ToArray()
        : [];

    internal RenderRun Run(params string[] extra) =>
        scenario.RunAt(Root, docker, CancellationToken.None, "next", ["--dry-run", .. extra]);

    public void Dispose() => scenario.Dispose();
}
