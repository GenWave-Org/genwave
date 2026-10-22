using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>Where a run keeps its files: <c>&lt;root&gt;/&lt;run-id&gt;/</c> with <c>raw</c>, <c>wav</c>, <c>gates</c> and the three json files.</summary>
internal sealed class RunLayout(string root, string runId)
{
    internal string RunId { get; } = runId;

    internal string RunDirectory { get; } = Path.Combine(root, runId);

    internal string RunJsonPath => Path.Combine(RunDirectory, "run.json");

    internal string TimingPath => Path.Combine(RunDirectory, "timing.jsonl");

    internal string GatesJsonPath => Path.Combine(RunDirectory, "gates.json");

    /// <summary>The engine's bytes as returned, before loudness matching.</summary>
    internal string RawPath(string slug, RenderLane lane, string lineId, string? suffix = null) =>
        Path.Combine(RunDirectory, "raw", slug, LaneFolder(lane), lineId + suffix + ".wav");

    /// <summary>The loudness-matched clip every later step reads.</summary>
    internal string WavPath(string slug, RenderLane lane, string lineId) =>
        Path.Combine(RunDirectory, "wav", slug, LaneFolder(lane), lineId + ".wav");

    /// <summary>A voiceSelect gate render (not a clip): <c>gates/&lt;slug&gt;/&lt;name&gt;.wav</c>.</summary>
    internal string GatePath(string slug, string name, bool raw = false) =>
        Path.Combine(RunDirectory, "gates", slug, raw ? name + ".raw.wav" : name + ".wav");

    internal static string LaneFolder(RenderLane lane) => lane.ToString().ToLowerInvariant();
}
