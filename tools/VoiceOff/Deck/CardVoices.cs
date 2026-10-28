using System.Globalization;

namespace GenWave.VoiceOff.Deck;

/// <summary>Where an engine's voices come from: a number of built-in voices, or the word <c>"clone"</c> or <c>"design"</c> in the card file. <see cref="Count"/> is only meaningful for <see cref="CardVoicesKind.Count"/>.</summary>
internal readonly record struct CardVoices
{
    internal const string CloneKeyword = "clone";

    internal const string DesignKeyword = "design";

    private CardVoices(CardVoicesKind kind, int count)
    {
        Kind = kind;
        Count = count;
    }

    internal CardVoicesKind Kind { get; }

    /// <summary>Always 0 unless <see cref="Kind"/> is <see cref="CardVoicesKind.Count"/>.</summary>
    internal int Count { get; }

    internal static CardVoices Cloned { get; } = new(CardVoicesKind.Clone, 0);

    internal static CardVoices Designed { get; } = new(CardVoicesKind.Design, 0);

    internal static CardVoices Builtin(int count) => new(CardVoicesKind.Count, count);

    /// <summary>The number, or the word, as the card file writes it.</summary>
    internal string Text => Kind switch
    {
        CardVoicesKind.Count => Count.ToString(CultureInfo.InvariantCulture),
        CardVoicesKind.Clone => CloneKeyword,
        CardVoicesKind.Design => DesignKeyword,
        _ => throw new InvalidOperationException($"Unknown voices kind {Kind}."),
    };
}
