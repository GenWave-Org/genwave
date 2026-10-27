using System.Globalization;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// Where a run keeps its files (F217.5): <c>&lt;root&gt;/&lt;run-id&gt;/</c> with <c>raw/&lt;card&gt;/&lt;n&gt;.wav</c>,
/// <c>cards/&lt;card&gt;.wav</c>, <c>renders.jsonl</c> and <c>run.json</c>. Card ids are kebab-case, so every path is one safe segment.
/// </summary>
internal sealed class RunLayout(string root, string runId)
{
    internal string RunId { get; } = runId;

    internal string RunDirectory { get; } = Path.Combine(root, runId);

    internal string RunJsonPath => Path.Combine(RunDirectory, "run.json");

    internal string JudgementPath => Path.Combine(RunDirectory, "judgement.json");

    internal string RendersPath => Path.Combine(RunDirectory, "renders.jsonl");

    /// <summary>The folder holding one card's raw paragraphs.</summary>
    internal string RawDirectory(string cardId) => Path.Combine(RunDirectory, "raw", cardId);

    /// <summary>The engine's bytes for paragraph <paramref name="n"/> as returned, before joining and loudness matching.</summary>
    internal string RawPath(string cardId, int n) =>
        Path.Combine(RawDirectory(cardId), n.ToString(CultureInfo.InvariantCulture) + ".wav");

    /// <summary>The loudness-matched clip every later step reads.</summary>
    internal string ClipPath(string cardId) => Path.Combine(RunDirectory, "cards", cardId + ".wav");

    /// <summary>Where the joined paragraphs wait for the loudness match; never survives a render.</summary>
    internal string JoinedPath(string cardId) => Path.Combine(RunDirectory, "cards", cardId + ".joined.tmp");
}
