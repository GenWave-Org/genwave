using System.Text;
using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>votes.jsonl</c>, append-only: one writer at a time, one line per vote, nothing rewritten. Reads wait for the writer so they never see half a line.
/// </summary>
internal sealed class VoteLog(string path, PairManifest manifest)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    internal async Task AppendAsync(Vote vote, CancellationToken ct)
    {
        byte[] line = Encoding.UTF8.GetBytes(StrictJson.WriteLine(vote) + "\n");
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

    /// <summary>The effective votes (last line per pair and judge), read through the same reader and rule <c>score</c> uses.</summary>
    internal async Task<Dictionary<string, Dictionary<string, VotePick>>> ReadEffectiveAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return EffectiveVotes.Last(await VotesReader.ReadAsync(path, "votes.jsonl", manifest, ct));
        }
        finally
        {
            gate.Release();
        }
    }
}
