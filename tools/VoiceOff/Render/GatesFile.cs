using System.Text.Json;

namespace GenWave.VoiceOff.Render;

/// <summary><c>gates.json</c>: <c>{ "engines": [ ... ] }</c>, one entry per selected profile in slug order, camelCase and indented.</summary>
internal sealed record GatesFile(IReadOnlyList<GateEntry> Engines)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
    };

    internal string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    internal Task WriteAsync(string path, CancellationToken ct) => AtomicTextFile.WriteAsync(path, ToJson(), ct);
}
