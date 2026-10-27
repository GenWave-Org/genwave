using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Render;

/// <summary>An append-only <c>timing.jsonl</c>: one JSON record per line, written whole so a crash can tear at most the last line.</summary>
internal sealed class TimingLog(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    internal async Task AppendAsync(TimingRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        DropTornTail(stream);
        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Json) + "\n"), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>A crash mid-write leaves a last line with no newline; cut it so the next record starts on a fresh line.</summary>
    private static void DropTornTail(FileStream stream)
    {
        long end = stream.Length;
        long keep = end;
        while (keep > 0)
        {
            stream.Seek(keep - 1, SeekOrigin.Begin);
            if (stream.ReadByte() == '\n')
            {
                break;
            }

            keep--;
        }

        if (keep != end)
        {
            stream.SetLength(keep);
        }
    }

    /// <summary>Every record in the file; a missing file is empty, and an unreadable last line (a crash mid-write) is dropped. An unreadable earlier line is corruption and throws.</summary>
    internal async Task<IReadOnlyList<TimingRecord>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines = (await File.ReadAllLinesAsync(path, ct)).Where(l => l.Length > 0).ToArray();
        List<TimingRecord> records = [];
        for (int i = 0; i < lines.Length; i++)
        {
            TimingRecord? record = TryParse(lines[i]);
            if (record is not null)
            {
                records.Add(record);
            }
            else if (i < lines.Length - 1)
            {
                throw new InvalidDataException($"{path} line {i + 1} is not a timing record.");
            }
        }

        return records;
    }

    private static TimingRecord? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<TimingRecord>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
