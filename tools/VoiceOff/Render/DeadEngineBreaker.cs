using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>Trips after <see cref="Limit"/> clips in a row where the engine did not answer (unreachable or timed out), so a dead engine cannot cost hours.</summary>
internal sealed class DeadEngineBreaker
{
    internal const int Limit = 3;

    private int strikes;

    internal bool Tripped => strikes >= Limit;

    /// <summary>Counts a clip: a no-answer failure is a strike, anything else (success, or a failure the engine did answer) resets the count.</summary>
    internal void Record(ClipOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome is { Ok: false, ErrorKind: SpeechErrorKind.Unreachable or SpeechErrorKind.Timeout })
        {
            strikes++;
        }
        else
        {
            strikes = 0;
        }
    }
}
