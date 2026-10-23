using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// Fixture runs in a temp root plus an engines folder (a baseline and challengers, made-up slugs), driven through <see cref="Cli.Run(string[], TextWriter, TextWriter)"/>.
/// <c>--out</c> goes into the temp root, so the repo's RESULTS.md is never touched.
/// </summary>
internal sealed class ScoreScenario : IDisposable
{
    private readonly EngineDirectoryFixture engineFolders = new();
    private readonly List<ScoreRunBuilder> runs = [];

    internal ScoreScenario(string baseline = "alpha", params string[] challengers)
    {
        Root = Path.Combine(Path.GetTempPath(), "voiceoff-score-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Baseline = baseline;
        List<(string, string)> files = [(baseline + ".json", EngineProfileJson.With(node => node["license"] = "MIT", baseline))];
        foreach (string slug in challengers.Length == 0 ? ["beta"] : challengers)
        {
            files.Add((slug + ".json", EngineProfileJson.Valid(slug, baseline: null).ToJsonString()));
        }

        EnginesDirectory = engineFolders.Create([.. files]);
    }

    internal string Root { get; }

    internal string Baseline { get; }

    internal string EnginesDirectory { get; }

    internal string Stdout { get; private set; } = string.Empty;

    internal string Stderr { get; private set; } = string.Empty;

    internal ScoreRunBuilder AddRun(EngineTier tier = EngineTier.Cpu, string startedAt = "2026-10-22T10:00:00Z", string hash = "hash-one")
    {
        ScoreRunBuilder run = new(Root, Baseline, tier, startedAt, hash);
        runs.Add(run);
        return run;
    }

    /// <summary>Writes every run, scores them all, and returns the exit code. <paramref name="outName"/> names the RESULTS file inside the temp root.</summary>
    internal int Score(string outName = "RESULTS.md", params string[] extraArgs)
    {
        foreach (ScoreRunBuilder run in runs)
        {
            run.Write();
        }

        List<string> args = ["score", "--root", Root, "--engines", EnginesDirectory, "--out", OutPath(outName)];
        args.AddRange(runs.SelectMany(r => new[] { "--run", r.FolderName }));
        args.AddRange(extraArgs);
        StringWriter output = new();
        StringWriter error = new();
        int exit = Cli.Run([.. args], output, error);
        Stdout = output.ToString();
        Stderr = error.ToString();
        return exit;
    }

    internal string OutPath(string name = "RESULTS.md") => Path.Combine(Root, name);

    internal string Results(string name = "RESULTS.md") => File.ReadAllText(OutPath(name));

    public void Dispose()
    {
        engineFolders.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
