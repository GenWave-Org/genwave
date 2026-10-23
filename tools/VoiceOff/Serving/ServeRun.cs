using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Serving;

/// <summary>A paired run opened for serving: the session book, the vote log, and the two files served by name. <c>manifest.json</c> is read for its pair ids only and never served.</summary>
internal sealed class ServeRun
{
    private ServeRun(string indexPath, SessionBook book, VoteLog votes)
    {
        IndexPath = indexPath;
        Book = book;
        Votes = votes;
    }

    internal string IndexPath { get; }

    internal SessionBook Book { get; }

    internal VoteLog Votes { get; }

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

        return new ServeRun(indexPath, SessionBook.Load(sessionDirectory), new VoteLog(Path.Combine(layout.RunDirectory, "votes.jsonl"), manifest));
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
