using GenWave.VoiceOff.Deck;

namespace GenWave.VoiceOff.Serving;

/// <summary>The F217.2 facts of a card as <c>/state</c> sends them. <paramref name="Voices"/> is a whole number or the word <c>clone</c> or <c>design</c>, as the card file writes it.</summary>
internal sealed record StateFacts(string Checkpoint, object Voices, bool Cloning, bool OwnVoice, IReadOnlyList<string> Cannot, IReadOnlyList<string> Tricks)
{
    internal static StateFacts From(CardFacts facts) =>
        new(
            facts.Checkpoint,
            facts.Voices.Kind is CardVoicesKind.Count ? facts.Voices.Count : facts.Voices.Text,
            facts.Cloning,
            facts.OwnVoice,
            facts.Cannot,
            facts.Tricks);
}
