namespace GenWave.VoiceOff.Listening;

/// <summary>Reads <c>winces.jsonl</c> in file order; the last line per reel counts.</summary>
internal static class WincesReader
{
    /// <exception cref="InvalidDataException">A line is malformed, has a negative count, or names a reel the manifest does not have.</exception>
    internal static Task<IReadOnlyList<WinceEntry>> ReadAsync(string path, string label, PairManifest manifest, CancellationToken ct)
    {
        HashSet<string> reelIds = manifest.Reels.Select(r => r.ReelId).ToHashSet(StringComparer.Ordinal);
        return JsonLines.ReadAsync<WinceEntry>(path, label, entry =>
        {
            if (entry.Count < 0)
            {
                return "the count is negative.";
            }

            return reelIds.Contains(entry.ReelId) ? null : $"reelId '{entry.ReelId}' is not in the manifest.";
        }, ct);
    }
}
