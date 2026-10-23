using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A finished render run in a temp root (run.json, gates.json, timing.jsonl and small real wavs, written with the production writers), plus the engines
/// and gauntlet folders it was made from. The slugs are made-up and distinctive so a grep miss means something. <see cref="Pair"/> drives <c>pair</c> through the CLI.
/// </summary>
internal sealed class PairScenario : IDisposable
{
    internal const string Baseline = "quillbase";
    internal const int DataBytes = 200;

    private readonly EngineDirectoryFixture engineFolders = new();
    private readonly GauntletFixtureDirectory gauntletFolders = new();
    private readonly PairFixtureOptions options;
    private readonly ToneClips tones = new();

    internal PairScenario(PairFixtureOptions? options = null)
    {
        this.options = options ?? new PairFixtureOptions();
        Challengers = this.options.Challengers ?? ["wrenfox", "oakmoth"];
        Root = Path.Combine(Path.GetTempPath(), "voiceoff-pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        LineIds = [.. Enumerable.Range(1, this.options.Lines).Select(n => "leadin-bubbly-" + n)];
        GauntletDirectory = gauntletFolders.Create(("leadins.json", GauntletFixtureDirectory.Lines(
            [.. LineIds.Select(id => GauntletFixtureDirectory.Line(id, "LeadIn", GauntletFixtureDirectory.EightWords))])));
        List<(string, string)> profiles = [(Baseline + ".json", EngineProfileJson.Valid(Baseline).ToJsonString())];
        profiles.AddRange(Challengers.Select(slug => (slug + ".json", EngineProfileJson.Valid(slug, baseline: null).ToJsonString())));
        EnginesDirectory = engineFolders.Create([.. profiles]);
        RunId = GenWave.VoiceOff.Render.RunId.Format(DateTimeOffset.Parse("2026-10-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), EngineTier.Cpu);
        RunDirectory = Path.Combine(Root, RunId);
        WriteRun();
    }

    internal IReadOnlyList<string> Challengers { get; }

    internal IReadOnlyList<string> LineIds { get; }

    internal string Root { get; }

    internal string RunId { get; }

    internal string RunDirectory { get; }

    internal string EnginesDirectory { get; }

    internal string GauntletDirectory { get; }

    internal string SessionDirectory => Path.Combine(RunDirectory, "session");

    internal string Stdout { get; private set; } = string.Empty;

    internal string Stderr { get; private set; } = string.Empty;

    internal IReadOnlyList<string> EverySlug => [Baseline, .. Challengers];

    internal int Pair(params string[] extraArgs) => Run(
        ["pair", "--run", RunId, "--root", Root, "--engines", EnginesDirectory, "--gauntlet", GauntletDirectory, "--seed", options.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), .. extraArgs]);

    internal int Reel(params string[] extraArgs) => Run(
        ["reel", "--run", RunId, "--root", Root, "--engines", EnginesDirectory, "--gauntlet", GauntletDirectory, .. extraArgs]);

    /// <summary>Builds the reels; a fixture that cannot reel is a broken fixture, not a failed fact.</summary>
    internal void MustReel()
    {
        if (Reel() != 0)
        {
            throw new InvalidOperationException("The fixture run did not reel: " + Stderr);
        }
    }

    internal string ReelsDirectory => Path.Combine(SessionDirectory, "reels");

    internal string ReelPath(string reelId) => Path.Combine(ReelsDirectory, reelId + ".wav");

    /// <summary>The engine behind each reel, from manifest.json.</summary>
    internal IReadOnlyList<ManifestReel> Reels() => Manifest().Reels;

    /// <summary>Casts one vote per plain-lane pair of the challenger: <paramref name="wins"/> for it, then <paramref name="losses"/> for the baseline.</summary>
    internal void VoteFor(string challenger, int wins, int losses)
    {
        List<string> lines = [];
        int cast = 0;
        foreach (ManifestPair pair in Manifest().Pairs.Where(p => p.Lane == RenderLane.Plain && (p.Truth.A == challenger || p.Truth.B == challenger)))
        {
            if (cast >= wins + losses)
            {
                break;
            }

            string challengerSide = pair.Truth.A == challenger ? "a" : "b";
            string baselineSide = challengerSide == "a" ? "b" : "a";
            lines.Add($$"""{"pairId":"{{pair.PairId}}","judge":"Dean","pick":"{{(cast < wins ? challengerSide : baselineSide)}}","at":"2026-10-22T12:00:00Z"}""");
            cast++;
        }

        File.AppendAllLines(Path.Combine(RunDirectory, "votes.jsonl"), lines);
    }

    internal int Run(string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exit = Cli.Run(args, output, error);
        Stdout = output.ToString();
        Stderr = error.ToString();
        return exit;
    }

    internal PairManifest Manifest() => StrictJson.Read<PairManifest>(File.ReadAllText(Path.Combine(RunDirectory, "manifest.json")));

    internal SessionFile Sessions() => StrictJson.Read<SessionFile>(File.ReadAllText(Path.Combine(SessionDirectory, "sessions.json")));

    /// <summary>Every file under session/, as paths relative to it.</summary>
    internal IReadOnlyList<string> SessionFiles() =>
        [.. Directory.GetFiles(SessionDirectory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(SessionDirectory, f))];

    /// <summary>Every file under session/ read as text (Latin-1 keeps wav bytes searchable), keyed by relative path.</summary>
    internal IReadOnlyDictionary<string, string> SessionContents() => SessionFiles().ToDictionary(
        f => f, f => System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(SessionDirectory, f))), StringComparer.Ordinal);

    /// <summary>The non-baseline engine behind each pair id.</summary>
    internal IReadOnlyDictionary<string, string> ChallengerByPair() =>
        Manifest().Pairs.ToDictionary(p => p.PairId, p => p.Truth.A == Baseline ? p.Truth.B : p.Truth.A);

    private void WriteRun()
    {
        Directory.CreateDirectory(RunDirectory);
        string hash = options.RunHashOverride ?? GauntletSet.Load(GauntletDirectory).ContentHash;
        DateTimeOffset started = DateTimeOffset.Parse("2026-10-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        new RunManifest(RunId, "cpu", "fixture-host", started, hash, false).WriteAsync(Path.Combine(RunDirectory, "run.json"), CancellationToken.None).GetAwaiter().GetResult();
        List<GateEntry> gates = [.. EverySlug.Select(GateFor)];
        new GatesFile(gates).WriteAsync(Path.Combine(RunDirectory, "gates.json"), CancellationToken.None).GetAwaiter().GetResult();
        RunLayout layout = new(Root, RunId);
        TimingLog log = new(layout.TimingPath);
        foreach (string slug in EverySlug)
        {
            foreach (RenderLane lane in slug == Baseline ? [RenderLane.Plain] : new[] { RenderLane.Plain, RenderLane.Markup })
            {
                for (int n = 1; n <= LineIds.Count; n++)
                {
                    WriteClip(layout, log, slug, lane, n);
                }
            }
        }
    }

    private void WriteClip(RunLayout layout, TimingLog log, string slug, RenderLane lane, int number)
    {
        string lineId = LineIds[number - 1];
        string key = $"{slug}/{lane.ToString().ToLowerInvariant()}/{number}";
        bool ok = !(options.FailedRenders ?? []).Contains(key) && number <= LineCap(slug);
        log.AppendAsync(new TimingRecord(slug, lane, lineId, EngineTier.Cpu, 1000, 2000, 100, null, ok, ok ? null : "boom"), CancellationToken.None).GetAwaiter().GetResult();

        // A failed render can leave a wav behind; only the timing record says it is no good.
        string path = layout.WavPath(slug, lane, lineId);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RunDirectory);
        // The engine slug and the line id ride in the LIST/INFO chunk, so a plain file copy would leak them into session/.
        byte[] tag = System.Text.Encoding.ASCII.GetBytes($"{slug} {lineId}");
        uint? listDeclared = (options.CorruptClips ?? []).Contains(key) ? uint.MaxValue : null;
        if (options.Tones)
        {
            tones.CopyTo(number, path);
            return;
        }

        File.WriteAllBytes(path, WavBytes.Make(8000, DataBytes, extraChunk: "LIST", listBody: tag, listDeclared: listDeclared));
    }

    private int LineCap(string slug) =>
        options.LinesByChallenger is not null && options.LinesByChallenger.TryGetValue(slug, out int cap) ? cap : int.MaxValue;

    private GateEntry GateFor(string slug) => new(
        slug,
        "MIT",
        true,
        null,
        (options.FailedContainers ?? []).Contains(slug) ? GateResult.Fail("container down") : GateResult.Pass("ok"),
        (options.FailedVoiceSelects ?? []).Contains(slug) ? GateResult.Fail("voices alike") : GateResult.Pass("ok"));

    public void Dispose()
    {
        tones.Dispose();
        engineFolders.Dispose();
        gauntletFolders.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
