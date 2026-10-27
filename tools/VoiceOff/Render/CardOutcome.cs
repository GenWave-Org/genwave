using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>How one card went. <see cref="Failure"/> is the engine's error kind when a paragraph failed on a request, so the breaker can tell a dead engine from a bad paragraph.</summary>
internal sealed record CardOutcome(bool Rendered, string? Reason, SpeechErrorKind? Failure = null)
{
    internal static CardOutcome Success() => new(true, null);

    internal static CardOutcome Failed(string reason, SpeechErrorKind? failure = null) => new(false, reason, failure);

    /// <summary>The engine did not answer at all (unreachable or timed out).</summary>
    internal bool NoAnswer => Failure is SpeechErrorKind.Unreachable or SpeechErrorKind.Timeout;
}
