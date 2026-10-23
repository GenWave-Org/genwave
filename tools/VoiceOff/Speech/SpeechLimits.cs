namespace GenWave.VoiceOff.Speech;

/// <summary>The most bytes the client will read from an engine before it gives up with <see cref="SpeechErrorKind.TooLarge"/>.</summary>
internal sealed record SpeechLimits(long MaxAudioBytes, long MaxVoicesBytes)
{
    internal static readonly SpeechLimits Default = new(64L * 1024 * 1024, 1024 * 1024);
}
