namespace GenWave.VoiceOff.Speech;

/// <summary>The plain-words label a failure reason carries for each <see cref="SpeechErrorKind"/>, shared by every place that reports one.</summary>
internal static class SpeechErrorKindLabel
{
    internal static string Label(this SpeechErrorKind kind) => kind switch
    {
        SpeechErrorKind.Unreachable => "unreachable",
        SpeechErrorKind.HttpStatus => "http status",
        SpeechErrorKind.Timeout => "timeout",
        SpeechErrorKind.EmptyBody => "empty body",
        SpeechErrorKind.TooLarge => "too large",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown speech error kind."),
    };
}
