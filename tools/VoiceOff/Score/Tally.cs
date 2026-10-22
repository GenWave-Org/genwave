namespace GenWave.VoiceOff.Score;

/// <summary>Wins, losses and ties of one challenger against the baseline. Ties are counted but never divided.</summary>
internal sealed record Tally(int Wins, int Losses, int Ties)
{
    internal static Tally Empty { get; } = new(0, 0, 0);

    /// <summary>The decided pairs: wins plus losses.</summary>
    internal int N => Wins + Losses;

    /// <summary>Wins over decided pairs; null when none were decided.</summary>
    internal double? Rate => N == 0 ? null : (double)Wins / N;
}
