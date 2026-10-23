using System.Text.Json;

namespace GenWave.VoiceOff.Listening;

/// <summary>Reads <c>manifest.json</c> and refuses anything the scorer could not trust.</summary>
internal static class ManifestReader
{
    /// <param name="knownEngines">Every engine slug that has a profile.</param>
    /// <exception cref="InvalidDataException">The file is malformed, has duplicate ids, or a pair does not pit the baseline against a known challenger.</exception>
    internal static async Task<PairManifest> ReadAsync(string path, string label, string baseline, IReadOnlySet<string> knownEngines, CancellationToken ct)
    {
        PairManifest manifest;
        try
        {
            manifest = StrictJson.Read<PairManifest>(await File.ReadAllTextAsync(path, ct));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} is not a valid manifest: {ex.Message}", ex);
        }

        Validate(manifest, label, baseline, knownEngines);
        return manifest;
    }

    private static void Validate(PairManifest manifest, string label, string baseline, IReadOnlySet<string> known)
    {
        HashSet<string> pairIds = new(StringComparer.Ordinal);
        foreach (ManifestPair pair in manifest.Pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.PairId) || !pairIds.Add(pair.PairId))
            {
                throw new InvalidDataException($"{label} has a blank or repeated pairId '{pair.PairId}'.");
            }

            string? problem = PairProblem(pair, baseline, known);
            if (problem is not null)
            {
                throw new InvalidDataException($"{label} pair {pair.PairId}: {problem}");
            }
        }

        HashSet<string> reelIds = new(StringComparer.Ordinal);
        HashSet<string> reelEngines = new(StringComparer.Ordinal);
        foreach (ManifestReel reel in manifest.Reels)
        {
            if (string.IsNullOrWhiteSpace(reel.ReelId) || !reelIds.Add(reel.ReelId))
            {
                throw new InvalidDataException($"{label} has a blank or repeated reelId '{reel.ReelId}'.");
            }

            if (!known.Contains(reel.Engine) || !reelEngines.Add(reel.Engine))
            {
                throw new InvalidDataException($"{label} reel {reel.ReelId}: engine '{reel.Engine}' is unknown or already has a reel.");
            }
        }
    }

    private static string? PairProblem(ManifestPair pair, string baseline, IReadOnlySet<string> known)
    {
        bool aIsBaseline = string.Equals(pair.Truth.A, baseline, StringComparison.Ordinal);
        bool bIsBaseline = string.Equals(pair.Truth.B, baseline, StringComparison.Ordinal);
        if (aIsBaseline == bIsBaseline)
        {
            return $"exactly one side must be the baseline engine '{baseline}'.";
        }

        if (!known.Contains(pair.Truth.A) || !known.Contains(pair.Truth.B))
        {
            return "a side names an engine with no profile.";
        }

        return string.IsNullOrWhiteSpace(pair.A) || string.IsNullOrWhiteSpace(pair.B) || string.IsNullOrWhiteSpace(pair.LineId)
            ? "a clip id or line id is blank."
            : null;
    }
}
