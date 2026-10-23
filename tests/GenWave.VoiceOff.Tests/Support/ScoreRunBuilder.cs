using System.Text.Json;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>One run folder of fixtures for <c>score</c>: run.json, gates.json, timing.jsonl, manifest.json, votes.jsonl and winces.jsonl.</summary>
internal sealed class ScoreRunBuilder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<object> pairs = [];
    private readonly List<object> reels = [];
    private readonly List<string> voteLines = [];
    private readonly List<string> winceLines = [];
    private readonly List<string> engines = [];
    private readonly Dictionary<string, string> failedContainers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> failedVoiceSelects = new(StringComparer.Ordinal);
    private readonly string baseline;
    private readonly string root;
    private readonly string runHash;
    private int pairCount;

    internal ScoreRunBuilder(string root, string baseline, EngineTier tier, string startedAt, string hash)
    {
        this.root = root;
        this.baseline = baseline;
        runHash = hash;
        Tier = tier;
        StartedAt = DateTimeOffset.Parse(startedAt, System.Globalization.CultureInfo.InvariantCulture);
        FolderName = GenWave.VoiceOff.Render.RunId.Format(StartedAt, tier);
        ManifestHash = hash;
    }

    internal EngineTier Tier { get; }

    internal string FolderName { get; }

    internal DateTimeOffset StartedAt { get; }

    internal string Host { get; set; } = "fixture-host";

    internal string ManifestHash { get; set; }

    internal bool WriteManifest { get; set; } = true;

    internal string RunDirectory => Path.Combine(root, FolderName);

    /// <summary>Adds an engine to gates.json; a gate-failed engine has no pairs.</summary>
    internal ScoreRunBuilder Engine(string slug)
    {
        if (!engines.Contains(slug))
        {
            engines.Add(slug);
        }

        return this;
    }

    /// <summary>Adds an engine whose container gate failed: it has a gates.json row, no timing records and no pairs.</summary>
    internal ScoreRunBuilder FailedEngine(string slug, string reason)
    {
        failedContainers[slug] = reason;
        return Engine(slug);
    }

    /// <summary>Adds an engine that passed its container gate but failed voice select: it still renders, so it has timing records, but no pairs.</summary>
    internal ScoreRunBuilder FailedVoiceSelect(string slug, string reason)
    {
        failedVoiceSelects[slug] = reason;
        return Engine(slug);
    }

    /// <summary>Pairs for the challenger in a lane: each judge decides the wins, losses and ties asked for; the challenger alternates sides.</summary>
    internal ScoreRunBuilder Votes(RenderLane lane, string engine, params JudgeTally[] judges)
    {
        foreach (JudgeTally tally in judges)
        {
            Decide(lane, engine, tally.Judge, "win", tally.Wins);
            Decide(lane, engine, tally.Judge, "loss", tally.Losses);
            Decide(lane, engine, tally.Judge, "tie", tally.Ties);
        }

        return this;
    }

    internal ScoreRunBuilder Plain(string engine, params JudgeTally[] judges) => Votes(RenderLane.Plain, engine, judges);

    internal ScoreRunBuilder Markup(string engine, params JudgeTally[] judges) => Votes(RenderLane.Markup, engine, judges);

    /// <summary>A pair with the challenger on side a and no votes; returns its id.</summary>
    internal string Pair(string engine, RenderLane lane = RenderLane.Plain) => AddPair(engine, lane, challengerOnA: true);

    internal ScoreRunBuilder Vote(string pairId, string judge, string pick) =>
        RawVote($$"""{"pairId":"{{pairId}}","judge":"{{judge}}","pick":"{{pick}}","at":"2026-10-22T12:00:00Z"}""");

    internal ScoreRunBuilder RawVote(string line)
    {
        voteLines.Add(line);
        return this;
    }

    internal ScoreRunBuilder RawWince(string line)
    {
        winceLines.Add(line);
        return this;
    }

    internal ScoreRunBuilder Reel(string engine, int? winces)
    {
        string reelId = "reel-" + engine;
        reels.Add(new { reelId, engine });
        if (winces is int count)
        {
            winceLines.Add(JsonSerializer.Serialize(new { reelId, count, at = "2026-10-22T13:00:00Z" }, Json));
        }

        return this;
    }

    internal void Write()
    {
        if (Directory.Exists(RunDirectory))
        {
            Directory.Delete(RunDirectory, recursive: true);
        }

        Directory.CreateDirectory(RunDirectory);
        new RunManifest(FolderName, GenWave.VoiceOff.Render.RunId.TierName(Tier), Host, StartedAt, runHash, false)
            .WriteAsync(Path.Combine(RunDirectory, "run.json"), CancellationToken.None).GetAwaiter().GetResult();
        List<GateEntry> gates = [GateFor(baseline), .. engines.Select(GateFor)];
        new GatesFile(gates).WriteAsync(Path.Combine(RunDirectory, "gates.json"), CancellationToken.None).GetAwaiter().GetResult();
        TimingLog log = new(Path.Combine(RunDirectory, "timing.jsonl"));
        foreach (string slug in gates.Where(g => g.Container?.Passed == true).Select(g => g.Slug))
        {
            log.AppendAsync(new TimingRecord(slug, RenderLane.Plain, "line-1", Tier, 3000, 20000, 900, null, true, null), CancellationToken.None).GetAwaiter().GetResult();
        }

        if (WriteManifest)
        {
            File.WriteAllText(
                Path.Combine(RunDirectory, "manifest.json"),
                JsonSerializer.Serialize(new { runId = FolderName, gauntletHash = ManifestHash, pairs, reels }, Json));
        }

        File.WriteAllLines(Path.Combine(RunDirectory, "votes.jsonl"), voteLines);
        File.WriteAllLines(Path.Combine(RunDirectory, "winces.jsonl"), winceLines);
    }

    private GateEntry GateFor(string slug) =>
        failedContainers.TryGetValue(slug, out string? reason)
            ? new GateEntry(slug, "MIT", true, null, GateResult.Fail(reason), null)
            : new GateEntry(slug, "MIT", true, null, GateResult.Pass("ok"), failedVoiceSelects.TryGetValue(slug, out string? voiceReason) ? GateResult.Fail(voiceReason) : GateResult.Pass("ok"));

    private void Decide(RenderLane lane, string engine, string judge, string outcome, int count)
    {
        for (int i = 0; i < count; i++)
        {
            bool challengerOnA = pairCount % 2 == 0;
            string pairId = AddPair(engine, lane, challengerOnA);
            string challengerSide = challengerOnA ? "a" : "b";
            string baselineSide = challengerOnA ? "b" : "a";
            Vote(pairId, judge, outcome switch
            {
                "win" => challengerSide,
                "loss" => baselineSide,
                _ => "tie",
            });
        }
    }

    private string AddPair(string engine, RenderLane lane, bool challengerOnA)
    {
        pairCount++;
        if (!engines.Contains(engine))
        {
            engines.Add(engine);
        }

        string pairId = "p" + pairCount;
        pairs.Add(new
        {
            pairId,
            lineId = "line-" + pairCount,
            lineClass = "short",
            lane = lane.ToString().ToLowerInvariant(),
            a = pairId + "-a",
            b = pairId + "-b",
            truth = new { a = challengerOnA ? engine : baseline, b = challengerOnA ? baseline : engine },
        });
        return pairId;
    }
}
