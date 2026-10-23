namespace GenWave.VoiceOff.Score;

/// <summary>The margin rule's answer and the decided pairs (n) it was given.</summary>
internal sealed record MarginResult(MarginOutcome Outcome, int N)
{
    internal bool Beats => Outcome == MarginOutcome.Beats;

    /// <summary>"insufficient pairs (n)" when there are too few decided pairs for any verdict, else null.</summary>
    internal string? InsufficientText => Outcome == MarginOutcome.Insufficient ? $"insufficient pairs ({N})" : null;
}
