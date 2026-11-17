namespace GenWave.Memory;

/// <summary>
/// Words that never count as a callback use (SPEC F218.45): every DJ's name, the station name, this break's
/// track, artist and album, and the speaking DJ's card text. Compared by <see cref="CallbackWordForms"/>, so "ballrooms" is excluded
/// when the card says "ballroom".
/// </summary>
public sealed class CallbackExclusions
{
    readonly HashSet<string> excludedBases;

    /// <param name="texts">Raw strings (names, titles, card text); null and blank entries are ignored.</param>
    public CallbackExclusions(IEnumerable<string?> texts)
    {
        excludedBases = CallbackWordForms.BasesOfAll(texts.SelectMany(text => RepetitionText.Tokens(text)));
    }

    /// <summary>True if <paramref name="token"/> (lowercased) is one of the excluded words or a form of one.</summary>
    public bool Contains(string token) => CallbackWordForms.Bases(token).Overlaps(excludedBases);
}
