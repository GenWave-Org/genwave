using System.Text.Json;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// <c>run.json</c> (SPEC F217.5): the script the run rendered, and per card where it rendered and whether it did.
/// <paramref name="StartedAt"/> is ISO-8601 UTC. Cards are kept in id order so the file diffs cleanly.
/// </summary>
internal sealed record RunManifest(
    string RunId,
    string ScriptHash,
    DateTimeOffset StartedAt,
    IReadOnlyDictionary<string, CardEntry> Cards)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>This manifest with <paramref name="cardId"/> set to <paramref name="entry"/>, replacing an earlier entry.</summary>
    internal RunManifest With(string cardId, CardEntry entry)
    {
        SortedDictionary<string, CardEntry> cards = new(StringComparer.Ordinal) { [cardId] = entry };
        foreach ((string id, CardEntry other) in Cards.Where(c => !string.Equals(c.Key, cardId, StringComparison.Ordinal)))
        {
            cards[id] = other;
        }

        return this with { Cards = cards };
    }

    internal Task WriteAsync(string path, CancellationToken ct) =>
        AtomicTextFile.WriteAsync(path, JsonSerializer.Serialize(this with { StartedAt = StartedAt.ToUniversalTime() }, Json) + "\n", ct);

    /// <exception cref="InvalidDataException">The file is not a run manifest.</exception>
    internal static async Task<RunManifest> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return JsonSerializer.Deserialize<RunManifest>(await File.ReadAllTextAsync(path, ct), Json)
                ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a run manifest: {ex.Message}", ex);
        }
    }
}
