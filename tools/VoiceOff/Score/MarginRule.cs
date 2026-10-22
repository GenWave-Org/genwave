namespace GenWave.VoiceOff.Score;

/// <summary>
/// SPEC F214.11: an engine beats the baseline when winRate is at least 0.65, every judge who decided a pair has a rate of at least 0.55,
/// and there are at least 40 decided pairs. Fewer decided pairs is "insufficient", never a verdict. Doubles, so exactly 0.65 passes.
/// </summary>
internal static class MarginRule
{
    internal const int MinDecidedPairs = 40;
    internal const double MinWinRate = 0.65;
    internal const double MinJudgeRate = 0.55;

    internal const string CueMapperRider = "adopt only with a cue mapper";

    internal static MarginResult Evaluate(Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        int n = cell.Total.N;
        if (n < MinDecidedPairs || cell.Total.Rate is not double rate)
        {
            return new MarginResult(MarginOutcome.Insufficient, n);
        }

        bool judgesAgree = cell.PerJudge.Values.All(t => t.Rate is not double own || own >= MinJudgeRate);
        return new MarginResult(rate >= MinWinRate && judgesAgree ? MarginOutcome.Beats : MarginOutcome.DoesNotBeat, n);
    }

    /// <summary>SPEC F214.12: clears the rule on markup but not on plain. It informs the row and never the verdict.</summary>
    internal static bool NeedsCueMapper(Cell plain, Cell markup) =>
        Evaluate(markup).Beats && !Evaluate(plain).Beats;
}
