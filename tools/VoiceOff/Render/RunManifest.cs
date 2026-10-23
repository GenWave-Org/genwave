using System.Text.Json;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// <c>run.json</c>: what RESULTS.md prints about a run (SPEC F214.8, F214.14). <paramref name="StartedAt"/> is ISO-8601 UTC.
/// <paramref name="CorrectionsPending"/> is true when the gauntlet still carries placeholder station corrections.
/// </summary>
internal sealed record RunManifest(
    string RunId,
    string Tier,
    string Host,
    DateTimeOffset StartedAt,
    string GauntletHash,
    bool CorrectionsPending)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal Task WriteAsync(string path, CancellationToken ct) =>
        AtomicTextFile.WriteAsync(path, JsonSerializer.Serialize(this with { StartedAt = StartedAt.ToUniversalTime() }, Json) + "\n", ct);
}
