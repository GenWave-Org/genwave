namespace GenWave.VoiceOff.Deck;

/// <summary>How an engine gets its voice: a fixed set of built-in voices, a clone of a clip, or a voice designed from a written description.</summary>
internal enum CardVoicesKind
{
    // No member is worth 0: an unset CardVoices must not read as a valid kind.
    Count = 1,
    Clone = 2,
    Design = 3,
}
