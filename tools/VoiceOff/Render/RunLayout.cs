namespace GenWave.VoiceOff.Render;

/// <summary>Where a run keeps its files: <c>&lt;root&gt;/&lt;run-id&gt;/</c> with <c>raw</c>, <c>wav</c>, <c>run.json</c> and <c>timing.jsonl</c>.</summary>
internal sealed class RunLayout(string root, string runId)
{
    internal string RunId { get; } = runId;

    internal string RunDirectory { get; } = Path.Combine(root, runId);

    internal string RunJsonPath => Path.Combine(RunDirectory, "run.json");

    internal string TimingPath => Path.Combine(RunDirectory, "timing.jsonl");

    /// <summary>The engine's bytes as returned, before loudness matching.</summary>
    internal string RawPath(string slug, string lineId, string? suffix = null) =>
        Path.Combine(RunDirectory, "raw", slug, lineId + suffix + ".wav");

    /// <summary>The loudness-matched clip every later step reads.</summary>
    internal string WavPath(string slug, string lineId) =>
        Path.Combine(RunDirectory, "wav", slug, lineId + ".wav");
}
