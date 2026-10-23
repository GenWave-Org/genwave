namespace GenWave.VoiceOff.Speech;

/// <summary>One render attempt: audio bytes and wall time on success, a typed error otherwise. Never thrown.</summary>
internal sealed record SpeechResult(ReadOnlyMemory<byte> Audio, double WallMilliseconds, SpeechError? Error)
{
    internal bool Ok => Error is null;
}
