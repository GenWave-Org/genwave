using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// The literal-regex compilation posture shared by every operator/persona-authored rule set (SPEC
/// F68.5, F97.3): the caller's own text is <see cref="Regex.Escape"/>d before compilation — never
/// treated as an arbitrary pattern — a <c>\b</c> anchor is added at an edge only when that edge
/// falls on a word-character boundary (so a literal that starts or ends with punctuation is not
/// force-anchored there), matching is case-insensitive and culture-invariant, and every compiled
/// rule carries a bounded match timeout so a pathological literal cannot hang the render/apply
/// path it runs on.
///
/// <para>
/// <see cref="SpeechCorrectionSet"/> and <see cref="PronunciationRuleSet"/> both compile through
/// this one helper so the posture the two promise to share (F97.3) cannot drift between them —
/// each owns only what differs (context-condition assertions, pattern/word span capture).
/// </para>
/// </summary>
internal static class LiteralRegexPosture
{
    /// <summary>
    /// How long a single match attempt may run before it is treated as pathological and skipped
    /// by the caller, rather than allowed to hang the render/apply path.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Compiles <paramref name="pattern"/> with this posture's fixed options: case-insensitive,
    /// culture-invariant, bounded by <see cref="MatchTimeout"/>. The caller is responsible for any
    /// <see cref="Regex.Escape"/>ing and boundary anchoring <paramref name="pattern"/> needs —
    /// this only fixes the options every literal-regex rule set in this project shares.
    /// </summary>
    public static Regex Compile(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>Whether <paramref name="value"/>'s first character is a word character (F68.5 anchoring).</summary>
    public static bool StartsWithWordChar(string value) => value.Length > 0 && IsWordChar(value[0]);

    /// <summary>Whether <paramref name="value"/>'s last character is a word character (F68.5 anchoring).</summary>
    public static bool EndsWithWordChar(string value) => value.Length > 0 && IsWordChar(value[^1]);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
