using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Scoring;

/// <summary>Loads one run folder for scoring: <c>run.json</c>, <c>gates.json</c>, <c>timing.jsonl</c>, <c>manifest.json</c>, and the optional votes and winces. Read-only.</summary>
internal static class ScoreRunLoader
{
    /// <exception cref="InvalidDataException">A file is missing, malformed, or disagrees with another file.</exception>
    internal static async Task<TierRun> LoadAsync(string root, string runId, EngineProfileSet profiles, CancellationToken ct)
    {
        RunLayout layout = new(root, runId);
        if (!Directory.Exists(layout.RunDirectory))
        {
            throw new InvalidDataException($"Run {runId} was not found under {root}.");
        }

        RunManifest info = Read<RunManifest>(layout.RunJsonPath, runId, "run.json", await ReadTextAsync(layout.RunJsonPath, runId, "run.json", ct));
        GatesFile gates = Read<GatesFile>(layout.GatesJsonPath, runId, "gates.json", await ReadTextAsync(layout.GatesJsonPath, runId, "gates.json", ct));
        EngineTier tier = CheckInfo(info, runId, gates, profiles);

        string manifestPath = Path.Combine(layout.RunDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException($"run {runId} has no manifest.json — run `voiceoff pair` first");
        }

        IReadOnlySet<string> known = profiles.Profiles.Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);
        PairManifest manifest = await ManifestReader.ReadAsync(manifestPath, $"run {runId} manifest.json", profiles.Baseline.Slug, known, ct);
        CheckManifest(manifest, info, runId);

        return new TierRun(
            runId,
            tier,
            info.Host,
            DateOnly.FromDateTime(info.StartedAt.UtcDateTime),
            info.GauntletHash,
            gates.Engines,
            await ReadTimingAsync(layout, runId, ct),
            manifest,
            await VotesReader.ReadAsync(Path.Combine(layout.RunDirectory, "votes.jsonl"), $"run {runId} votes.jsonl", manifest, ct),
            await WincesReader.ReadAsync(Path.Combine(layout.RunDirectory, "winces.jsonl"), $"run {runId} winces.jsonl", manifest, ct));
    }

    private static async Task<string> ReadTextAsync(string path, string runId, string name, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : throw new InvalidDataException($"run {runId} has no {name}.");

    private static T Read<T>(string path, string runId, string name, string text)
        where T : class
    {
        try
        {
            return StrictJson.Read<T>(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"run {runId} {name} is not valid: {ex.Message}", ex);
        }
    }

    private static EngineTier CheckInfo(RunManifest info, string runId, GatesFile gates, EngineProfileSet profiles)
    {
        if (!string.Equals(info.RunId, runId, StringComparison.Ordinal) || !RunId.TryParseTier(runId, out EngineTier tier)
            || !string.Equals(info.Tier, RunId.TierName(tier), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"run {runId} run.json names run '{info.RunId}' and tier '{info.Tier}', which do not match the folder.");
        }

        GateEntry? stranger = gates.Engines.FirstOrDefault(g => profiles.Profiles.All(p => !string.Equals(p.Slug, g.Slug, StringComparison.Ordinal)));
        return stranger is null
            ? tier
            : throw new InvalidDataException($"run {runId} gates.json names engine '{stranger.Slug}', which has no profile.");
    }

    private static void CheckManifest(PairManifest manifest, RunManifest info, string runId)
    {
        if (!string.Equals(manifest.RunId, runId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"run {runId} manifest.json belongs to run '{manifest.RunId}'.");
        }

        if (!string.Equals(manifest.GauntletHash, info.GauntletHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"run {runId} manifest.json has gauntlet hash {manifest.GauntletHash} but run.json has {info.GauntletHash}; run `voiceoff pair` again.");
        }
    }

    private static async Task<IReadOnlyList<TimingRecord>> ReadTimingAsync(RunLayout layout, string runId, CancellationToken ct)
    {
        try
        {
            return await new TimingLog(layout.TimingPath).ReadAsync(ct);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"run {runId} timing.jsonl is not valid: {ex.Message}", ex);
        }
    }
}
