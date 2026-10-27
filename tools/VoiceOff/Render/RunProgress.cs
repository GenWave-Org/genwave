namespace GenWave.VoiceOff.Render;

/// <summary>The run's progress book: paragraphs planned, paragraphs done and the wall time of the real renders, across cards.</summary>
internal sealed class RunProgress(int plannedParagraphs)
{
    private readonly List<double> doneWallMs = [];
    private int planned = plannedParagraphs;
    private int done;

    /// <summary>Paragraphs that will not be rendered (a failed gate, a stopped engine, a failed card): they leave the total.</summary>
    internal void Drop(int paragraphs) => planned = Math.Max(planned - paragraphs, done);

    internal string NextLine() => RenderProgress.Line(done + 1, planned, doneWallMs);

    /// <summary>A paragraph came back from the engine, in <paramref name="wallMs"/>.</summary>
    internal void Finished(double wallMs)
    {
        done++;
        doneWallMs.Add(wallMs);
    }
}
