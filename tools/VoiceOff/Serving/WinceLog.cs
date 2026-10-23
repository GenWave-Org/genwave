using System.Text;
using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>winces.jsonl</c>, append-only like <see cref="VoteLog"/>: one writer at a time, one line per entered count, nothing rewritten. The last line per reel counts.
/// </summary>
internal sealed class WinceLog(string path, PairManifest manifest)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    internal async Task AppendAsync(WinceEntry entry, CancellationToken ct)
    {
        byte[] line = Encoding.UTF8.GetBytes(StrictJson.WriteLine(entry) + "\n");
        await gate.WaitAsync(ct);
        try
        {
            await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync(line, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The counted wince total of each reel that has one, read through the same reader <c>score</c> uses.</summary>
    internal async Task<IReadOnlyDictionary<string, int>> ReadCountsAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            IReadOnlyList<WinceEntry> entries = await WincesReader.ReadAsync(path, "winces.jsonl", manifest, ct);
            return entries.GroupBy(e => e.ReelId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Count, StringComparer.Ordinal);
        }
        finally
        {
            gate.Release();
        }
    }
}
