using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Deck;

/// <summary>Every card in a <c>cards</c> directory, checked against each other and against the engine profiles.</summary>
internal sealed class CardSet
{
    private CardSet(IReadOnlyList<Card> cards)
    {
        Cards = cards;
    }

    internal IReadOnlyList<Card> Cards { get; }

    internal Card AsShipped => Cards.Single(c => c.AsShipped);

    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="InvalidDataException">A card is malformed, an id repeats, an engine or sibling does not resolve, or the set does not have exactly one as-shipped card.</exception>
    internal static CardSet Load(string directory, EngineProfileSet engines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Cards directory not found: {directory}");
        }

        List<Card> cards = [];
        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            Card card = CardReader.Parse(File.ReadAllText(path), name);
            if (cards.Any(c => string.Equals(c.Id, card.Id, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Card id {card.Id} appears more than once (second in {name}).");
            }

            cards.Add(card);
        }

        foreach (Card card in cards)
        {
            RequireResolves(card, cards, engines);
        }

        RequireOneAsShipped(cards, directory);
        return new CardSet(cards);
    }

    private static void RequireResolves(Card card, List<Card> cards, EngineProfileSet engines)
    {
        if (!engines.Profiles.Any(p => string.Equals(p.Slug, card.Engine, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Card {card.Id}: engine \"{card.Engine}\" has no profile in engines/.");
        }

        if (card.VariantOf is not null
            && (string.Equals(card.VariantOf, card.Id, StringComparison.Ordinal)
                || !cards.Any(c => string.Equals(c.Id, card.VariantOf, StringComparison.Ordinal))))
        {
            throw new InvalidDataException($"Card {card.Id}: variantOf \"{card.VariantOf}\" is not another card in the set.");
        }
    }

    private static void RequireOneAsShipped(List<Card> cards, string directory)
    {
        string[] shipped = cards.Where(c => c.AsShipped).Select(c => c.Id).ToArray();
        if (shipped.Length != 1)
        {
            throw new InvalidDataException(shipped.Length == 0
                ? $"No card in {directory} has asShipped true; exactly one must."
                : $"Cards {string.Join(", ", shipped)} all have asShipped true; exactly one must.");
        }
    }
}
