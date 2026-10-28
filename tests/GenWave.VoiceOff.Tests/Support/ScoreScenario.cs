using System.Globalization;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A scored run on disk and the real <c>score</c> verb run over it through <see cref="Cli"/>: four cards (<see cref="One"/>, <see cref="Two"/>, <see cref="Three"/> rendered, <see cref="Four"/> failed),
/// their card files, one engine profile, a script, renders.jsonl and, unless left out, a judgement. The first run writes <c>RESULTS.md</c> into the scenario folder.
/// </summary>
internal sealed class ScoreScenario : IDisposable
{
    internal const string One = "a-one";
    internal const string Two = "b-two";
    internal const string Three = "c-three";
    internal const string Four = "d-four";
    internal const string FailureReason = "container was down";

    /// <summary>Written as 23:30 at UTC-5 so the date line proves the date is the UTC one (2026-10-28).</summary>
    internal static readonly DateTimeOffset Started = DateTimeOffset.Parse("2026-10-27T23:30:00-05:00", CultureInfo.InvariantCulture);

    /// <summary>Written verbatim by the judge: a cell separator, a heading and a code fence, none of which may break the page.</summary>
    internal const string AwkwardNote = "Warm.\n| not a cell\n# not a heading\n```\nnot a fence";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "voiceoff-score-" + Guid.NewGuid().ToString("N"));
    private readonly EngineDirectoryFixture engines = new();
    private readonly CardFixtureDirectory cards = new();
    private readonly string enginesDirectory;
    private readonly string cardsDirectory;
    private readonly StringWriter output = new();
    private readonly StringWriter errorOutput = new();

    /// <param name="order">The judgement order; null leaves judgement.json out.</param>
    /// <param name="scriptHashInRun">What run.json records as the script hash; null records the hash of the script on disk.</param>
    /// <param name="remarks">The judgement's top-level <c>remarks</c> member as raw JSON (so a wrong type can be tried); null leaves the member out.</param>
    internal ScoreScenario(string[]? order, string? scriptHashInRun = null, JsonNode? remarks = null)
    {
        Directory.CreateDirectory(RunDirectory);
        ScriptPath = Path.Combine(folder, "script.json");
        File.WriteAllText(ScriptPath, """{ "version": "1", "paragraphs": [ { "n": 1, "text": "Hello there." } ] }""");
        ScriptHash = ScriptFile.Load(ScriptPath).Hash;

        enginesDirectory = engines.Create(("alpha.json", EngineProfileJson.Valid("alpha").ToJsonString()));
        cardsDirectory = cards.Create(
            (One + ".json", CardFixtureDirectory.CardJson(One, "alpha", host: "thor-cpu", extraMembers: "\"asShipped\": true,")),
            (Two + ".json", CardFixtureDirectory.CardJson(Two, "alpha", host: "runes-gpu")),
            (Three + ".json", CardFixtureDirectory.CardJson(Three, "alpha", host: "thor-gpu")),
            (Four + ".json", CardFixtureDirectory.CardJson(Four, "alpha", host: "thor-cpu")));

        Dictionary<string, CardEntry> runCards = new(StringComparer.Ordinal)
        {
            [One] = new CardEntry("thor-cpu", true),
            [Two] = new CardEntry("runes-gpu", true),
            [Three] = new CardEntry("thor-gpu", true),
            [Four] = new CardEntry("thor-cpu", false, FailureReason),
        };
        new RunManifest("scored", scriptHashInRun ?? ScriptHash, Started, runCards)
            .WriteAsync(Path.Combine(RunDirectory, "run.json"), CancellationToken.None).GetAwaiter().GetResult();
        RenderLog log = new(Path.Combine(RunDirectory, "renders.jsonl"));
        log.AppendAsync(new RenderRecord(One, 1, 12, 1500, 1000, true, null), CancellationToken.None).GetAwaiter().GetResult();
        log.AppendAsync(new RenderRecord(One, 2, 12, 2800, 1000, true, null), CancellationToken.None).GetAwaiter().GetResult();
        log.AppendAsync(new RenderRecord(Four, 1, 12, 10, 0, false, "down"), CancellationToken.None).GetAwaiter().GetResult();

        if (order is not null)
        {
            File.WriteAllText(Path.Combine(RunDirectory, "judgement.json"), JudgementJson(order, remarks));
        }

        ResultsPath = Path.Combine(folder, "RESULTS.md");
        Exit = Score(ResultsPath);
    }

    internal string RunDirectory => Path.Combine(folder, "scored");

    internal string ScriptPath { get; }

    internal string ScriptHash { get; }

    internal string ResultsPath { get; }

    internal int Exit { get; }

    internal string Stdout => output.ToString();

    internal string Stderr => errorOutput.ToString();

    internal string JudgementPath => Path.Combine(RunDirectory, "judgement.json");

    /// <summary>The file the first run wrote, or empty when it wrote none.</summary>
    internal string Results => File.Exists(ResultsPath) ? File.ReadAllText(ResultsPath) : string.Empty;

    /// <summary>Runs <c>score</c> again over the same files, writing <paramref name="outPath"/>.</summary>
    internal int Score(string outPath) =>
        Cli.Run(
            ["score", "--run", "scored", "--root", folder, "--engines", enginesDirectory, "--cards", cardsDirectory, "--script", ScriptPath, "--out", outPath],
            output,
            errorOutput);

    /// <summary>The data rows of the table under the <c>## </c> heading containing <paramref name="heading"/>, each as its trimmed cells.</summary>
    internal IReadOnlyList<string[]> Table(string heading) =>
        [.. Section(heading).Where(l => l.StartsWith('|')).Skip(2).Select(l => l.Trim('|').Split(" | ").Select(c => c.Trim()).ToArray())];

    internal IReadOnlyList<string> Section(string heading)
    {
        string[] lines = Results.Split('\n');
        return [.. lines.SkipWhile(l => !(l.StartsWith("## ", StringComparison.Ordinal) && l.Contains(heading, StringComparison.Ordinal)))
            .Skip(1)
            .TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal))];
    }

    private static string JudgementJson(string[] order, JsonNode? remarks)
    {
        JsonObject cardEntries = new()
        {
            [One] = new JsonObject { ["winces"] = 2, ["stars"] = 4, ["notes"] = "warm" },
            [Two] = new JsonObject { ["winces"] = 3, ["stars"] = null, ["notes"] = "" },
            [Three] = new JsonObject { ["winces"] = 0, ["stars"] = 5, ["notes"] = AwkwardNote },
        };
        JsonObject judgement = new()
        {
            ["savedAt"] = "2026-10-27T12:00:00Z",
            ["order"] = new JsonArray([.. order.Select(o => JsonValue.Create(o))]),
            ["cards"] = cardEntries,
        };
        if (remarks is not null)
        {
            judgement["remarks"] = remarks;
        }

        return judgement.ToJsonString();
    }

    public void Dispose()
    {
        engines.Dispose();
        cards.Dispose();
        Directory.Delete(folder, recursive: true);
    }
}
