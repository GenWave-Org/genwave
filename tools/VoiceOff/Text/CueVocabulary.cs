using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// The inline <c>{name}</c> cue vocabulary: <see cref="Script"/> is the seven script cues that card cue maps are checked against.
/// </summary>
internal static partial class CueVocabulary
{
    /// <summary>The seven cues the one Voice Off script writes inline (SPEC F217.1) and a card's cue map may key.</summary>
    internal static readonly IReadOnlySet<string> Script =
        new HashSet<string>(["breath", "laugh", "um", "cough", "giggle", "sigh", "pause"], StringComparer.Ordinal);

    /// <summary>The one definition of cue syntax: a brace token whose group 1 is the cue name.</summary>
    internal const string TokenPattern = @"\{([^{}\s]*)\}";

    /// <summary>Matches any brace token; group 1 is the cue name.</summary>
    [GeneratedRegex(TokenPattern)]
    internal static partial Regex Token();
}
