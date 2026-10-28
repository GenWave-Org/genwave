using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Serving;

/// <summary>Reads and writes <c>judgement.json</c>. Writes are atomic and one at a time, so a reader sees a whole document and two quick saves cannot share a temp file.</summary>
internal sealed class JudgementStore(string path, TimeProvider time)
{
    private readonly SemaphoreSlim writing = new(1, 1);

    /// <returns>The saved judgement, or null when nothing was saved yet.</returns>
    /// <exception cref="InvalidDataException">The file is not a judgement.</exception>
    internal async Task<Judgement?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return StrictJson.Read<Judgement>(await File.ReadAllTextAsync(path, ct));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid judgement: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes the request as the whole judgement, stamped with the server's clock (UTC), cards in id order. The request has no remarks (the page never sends
    /// them), so the saved ones are carried over rather than wiped.
    /// </summary>
    internal async Task<Judgement> SaveAsync(JudgementRequest request, CancellationToken ct)
    {
        await writing.WaitAsync(ct);
        try
        {
            Judgement judgement = new(
                time.GetUtcNow(),
                [.. request.Order],
                new SortedDictionary<string, CardJudgement>(request.Cards.ToDictionary(c => c.Key, c => c.Value), StringComparer.Ordinal),
                await SavedRemarksAsync(ct));
            await AtomicTextFile.WriteAsync(path, StrictJson.Write(judgement), ct);
            return judgement;
        }
        finally
        {
            writing.Release();
        }
    }

    // A file that cannot be read has no remarks worth keeping, and a save is how the judge recovers from it.
    private async Task<string?> SavedRemarksAsync(CancellationToken ct)
    {
        try
        {
            return (await ReadAsync(ct))?.Remarks;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
