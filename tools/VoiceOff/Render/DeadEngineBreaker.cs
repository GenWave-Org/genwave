namespace GenWave.VoiceOff.Render;

/// <summary>Trips after <see cref="Limit"/> cards in a row where the engine did not answer (unreachable or timed out), so a dead engine cannot cost hours.</summary>
internal sealed class DeadEngineBreaker
{
    internal const int Limit = 3;

    private int strikes;

    internal bool Tripped => strikes >= Limit;

    /// <summary>Counts a card: a no-answer failure is a strike, anything else (success, or a failure the engine did answer) resets the count.</summary>
    internal void Record(CardOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        strikes = outcome.NoAnswer ? strikes + 1 : 0;
    }
}
