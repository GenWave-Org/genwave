using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// The inline <c>{name}</c> cue vocabularies. <see cref="Known"/> is the gauntlet / engine-profile markup cues (retired with the gauntlet);
/// <see cref="Script"/> is the seven script cues that card cue maps are checked against.
/// </summary>
internal static partial class CueVocabulary
{
    internal static readonly IReadOnlySet<string> Known =
        new HashSet<string>(["pause", "laugh", "sigh", "breath", "um", "emphasis"], StringComparer.Ordinal);

    /// <summary>The seven cues the one Voice Off script writes inline (SPEC F217.1) and a card's cue map may key.</summary>
    internal static readonly IReadOnlySet<string> Script =
        new HashSet<string>(["breath", "laugh", "um", "cough", "giggle", "sigh", "pause"], StringComparer.Ordinal);

    /// <summary>The one definition of cue syntax: a brace token whose group 1 is the cue name.</summary>
    internal const string TokenPattern = @"\{([^{}\s]*)\}";

    /// <summary>Matches any brace token; group 1 is the cue name.</summary>
    [GeneratedRegex(TokenPattern)]
    internal static partial Regex Token();

    /// <summary>
    /// The text with every cue token replaced by a space. Serves only the retired gauntlet path
    /// (<c>CueText</c>, <c>GauntletLineParser</c>) and goes with it (F217.12); card text drops cues
    /// through <c>CardText.ReplaceCues</c>.
    /// </summary>
    internal static string Strip(string text) => Token().Replace(text, " ");
}
