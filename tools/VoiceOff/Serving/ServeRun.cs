using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Reeling;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Serving;

/// <summary>A paired run opened for serving: the session book, the vote and wince logs, the reels, and the two files served by name. <c>manifest.json</c> itself is never served.</summary>
internal sealed partial class ServeRun
{
    private readonly IReadOnlyDictionary<string, string> reelFiles;

    private ServeRun(string indexPath, SessionBook book, VoteLog votes, WinceLog winces, IReadOnlyList<ManifestReel> reels, IReadOnlyDictionary<string, string> reelFiles)
    {
        IndexPath = indexPath;
        Book = book;
        Votes = votes;
        Winces = winces;
        Reels = reels;
        this.reelFiles = reelFiles;
    }

    internal string IndexPath { get; }

    internal SessionBook Book { get; }

    internal VoteLog Votes { get; }

    internal WinceLog Winces { get; }

    /// <summary>The reels of the manifest, in its order; empty when <c>reel</c> has not run. This is the only place the reel-to-engine map is read.</summary>
    internal IReadOnlyList<ManifestReel> Reels { get; }

    /// <summary>The wav of a reel, found by manifest reel id only, never by a path from the request.</summary>
    internal bool TryGetReelFile(string reelId, [NotNullWhen(true)] out string? file)
    {
        file = null;
        if (!reelFiles.TryGetValue(reelId, out string? path) || !File.Exists(path))
        {
            return false;
        }

        file = path;
        return true;
    }

    [GeneratedRegex(@"\A[0-9a-f]{32}\z")]
    private static partial Regex ReelIdShape();

    private static Dictionary<string, string> ReelFiles(PairManifest manifest, string reelsDirectory)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (ManifestReel reel in manifest.Reels)
        {
            if (!ReelIdShape().IsMatch(reel.ReelId))
            {
                throw new InvalidDataException("manifest.json lists a reel id that is not 32 lowercase hex characters.");
            }

            files[reel.ReelId] = Path.Combine(reelsDirectory, reel.ReelId + ".wav");
        }

        return files;
    }

    /// <exception cref="InvalidDataException">The run is missing, is not paired, or a file of it is bad.</exception>
    internal static ServeRun Open(string root, string runId)
    {
        RunLayout layout = new(root, runId);
        if (!Directory.Exists(layout.RunDirectory))
        {
            throw new InvalidDataException($"Run {runId} was not found under {root}.");
        }

        string manifestPath = Path.Combine(layout.RunDirectory, "manifest.json");
        string sessionDirectory = SessionFolder.PathIn(layout);
        if (!File.Exists(manifestPath) || !Directory.Exists(sessionDirectory))
        {
            throw new InvalidDataException($"run {runId} is not paired — run `voiceoff pair --run {runId}` first");
        }

        PairManifest manifest = ReadManifest(manifestPath);
        string indexPath = Path.Combine(sessionDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new InvalidDataException($"{sessionDirectory} has no index.html.");
        }

        return new ServeRun(
            indexPath,
            SessionBook.Load(sessionDirectory),
            new VoteLog(Path.Combine(layout.RunDirectory, "votes.jsonl"), manifest),
            new WinceLog(Path.Combine(layout.RunDirectory, "winces.jsonl"), manifest),
            manifest.Reels,
            ReelFiles(manifest, ReelBuilder.PathIn(layout)));
    }

    private static PairManifest ReadManifest(string path)
    {
        try
        {
            return StrictJson.Read<PairManifest>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"manifest.json is not valid: {ex.Message}", ex);
        }
    }
}
