namespace GenWave.VoiceOff.Deck;

/// <summary>How many built-in voices an engine has, or that it has none and clones one (<c>"clone"</c> in the card file).</summary>
internal readonly record struct CardVoices(int? Count)
{
    internal const string CloneKeyword = "clone";

    internal static CardVoices Cloned { get; } = new(null);

    internal bool IsClone => Count is null;
}
