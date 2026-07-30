namespace GenWave.Tts;

/// <summary>
/// One operator-authored pronunciation correction: replace <see cref="From"/> with <see cref="To"/>
/// wherever it appears in booth-bound text. Compiled and matched by <see cref="SpeechCorrectionSet"/>;
/// this record only carries the raw operator data (SPEC F68.5).
///
/// <para>
/// The two optional context conditions (gh-#161) make a rule heteronym-safe: when
/// <see cref="WhenFollowedBy"/> and/or <see cref="WhenPrecededBy"/> is set, the rule fires only
/// where that neighbouring-word condition holds (e.g. <c>wind → wynd</c> only when followed by
/// <c>down|up</c>), leaving every other occurrence untouched. Each is a <c>|</c>-separated list of
/// literal words/phrases — operator text, never a pattern; both null/blank (the wire shape every
/// pre-gh-#161 rule has) means the rule is unconditional and behaves exactly as before. When both
/// are set, both must hold. Compilation semantics (word boundaries, case-insensitivity,
/// sentence-boundary limits) live in <see cref="SpeechCorrectionSet"/>.
/// </para>
/// </summary>
public sealed record SpeechCorrection(string From, string To)
{
    /// <summary><c>|</c>-separated literal words/phrases; the rule fires only when the match is
    /// immediately preceded by one of them (whitespace/punctuation between is fine, a sentence end
    /// is not). Null or blank — including every rule authored before gh-#161 — means no
    /// preceded-by condition.</summary>
    public string? WhenPrecededBy { get; init; }

    /// <summary><c>|</c>-separated literal words/phrases; the rule fires only when the match is
    /// immediately followed by one of them (whitespace/punctuation between is fine, a sentence end
    /// is not). Null or blank — including every rule authored before gh-#161 — means no
    /// followed-by condition.</summary>
    public string? WhenFollowedBy { get; init; }
}
