namespace GenWave.VoiceOff.Speech;

/// <summary>An engine's voice listing, or the typed error that stopped it. Never thrown.</summary>
internal sealed record VoiceListResult(IReadOnlyList<string> VoiceIds, SpeechError? Error)
{
    internal bool Ok => Error is null;
}
