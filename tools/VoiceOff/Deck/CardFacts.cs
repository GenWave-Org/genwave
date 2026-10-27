namespace GenWave.VoiceOff.Deck;

/// <summary>What a card tells the listener about its engine setup (SPEC F217.2).</summary>
internal sealed record CardFacts(
    string Checkpoint,
    CardVoices Voices,
    bool Cloning,
    bool OwnVoice,
    IReadOnlyList<string> Cannot,
    IReadOnlyList<string> Tricks);
