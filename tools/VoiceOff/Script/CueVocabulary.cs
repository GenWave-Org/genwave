using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Script;

/// <summary>The abstract cue markers a gauntlet line may carry, written inline as <c>{name}</c>.</summary>
internal static partial class CueVocabulary
{
    internal static readonly IReadOnlySet<string> Known =
        new HashSet<string>(["pause", "laugh", "sigh", "breath", "um", "emphasis"], StringComparer.Ordinal);

    /// <summary>Matches any brace token; group 1 is the cue name.</summary>
    [GeneratedRegex(@"\{([^{}\s]*)\}")]
    internal static partial Regex Token();

    /// <summary>The text with every cue token replaced by a space.</summary>
    internal static string Strip(string text) => Token().Replace(text, " ");
}
