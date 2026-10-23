using System.Text.Json;

namespace GenWave.VoiceOff.Listening;

/// <summary>Reads a <c>.jsonl</c> file whole. Any bad line, blank lines included, refuses the file and names it and its 1-based line number.</summary>
internal static class JsonLines
{
    /// <summary>A missing file is an empty list.</summary>
    /// <exception cref="InvalidDataException">A line is not a valid record.</exception>
    internal static async Task<IReadOnlyList<T>> ReadAsync<T>(string path, string label, Func<T, string?> check, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(path, ct);
        List<T> records = [];
        for (int i = 0; i < lines.Length; i++)
        {
            records.Add(Parse(lines[i], label, i + 1, check));
        }

        return records;
    }

    private static T Parse<T>(string line, string label, int number, Func<T, string?> check)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidDataException($"{label} line {number} is blank.");
        }

        T record;
        try
        {
            record = StrictJson.Read<T>(line);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} line {number} is not a valid record: {ex.Message}", ex);
        }

        string? problem = check(record);
        return problem is null ? record : throw new InvalidDataException($"{label} line {number}: {problem}");
    }
}
