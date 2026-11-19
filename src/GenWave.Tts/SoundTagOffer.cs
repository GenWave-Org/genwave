namespace GenWave.Tts;

/// <summary>
/// The sound tags a DJ prompt offers and how many one break may carry (SPEC F219.16). Resolved per prompt by
/// <see cref="ISoundTagOfferProvider"/>; a builder given none writes the prompt exactly as before tags existed.
/// </summary>
public sealed record SoundTagOffer(IReadOnlyList<string> Tags, int Cap)
{
    /// <summary>
    /// The prompt line (no commas, per the scaffold's comma rule). Names tags as the one exception to "no stage
    /// directions" and forbids any other bracketed word.
    /// </summary>
    public string PromptLine => PromptLineFor("");

    /// <summary>The same line for the two-DJ prompt, where the cap spans the whole exchange.</summary>
    public string ExchangePromptLine => PromptLineFor(" in the whole exchange");

    string PromptLineFor(string scope) =>
        $"Sound tags are the one allowed exception to the rules above. Use at most {Cap} of these exact tags{scope}: {string.Join(' ', Tags)}. " +
        "Write a tag exactly as listed and put it inline where the sound would happen. " +
        "Never put any other word in brackets.";
}
