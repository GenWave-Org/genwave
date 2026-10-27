using GenWave.VoiceOff.Deck;

namespace GenWave.VoiceOff.Serving;

/// <summary>What the card file says about a card: the page takes engine names and facts from here, never from its HTML (SPEC F217.13).</summary>
internal sealed record StateCardDetails(
    string Engine, string Title, string? VariantOf, bool AsShipped, string Voice, double Speed, string Why, StateFacts Facts)
{
    internal static StateCardDetails From(Card card) =>
        new(card.Engine, card.Title, card.VariantOf, card.AsShipped, card.Voice, card.Speed, card.Why, StateFacts.From(card.Facts));
}
