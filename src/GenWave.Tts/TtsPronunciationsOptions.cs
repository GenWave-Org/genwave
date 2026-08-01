namespace GenWave.Tts;

/// <summary>
/// Binds the raw <c>Tts:Pronunciations</c> configuration leaf (SPEC F97.3, ARCHITECTURE.md "Make
/// the DJs sound human") — a JSON-encoded array of <c>{pattern, word, ipa}</c> station-authored
/// pronunciation rules, e.g. <c>[{"pattern":"Reykjavík","ipa":"/ˈreɪkjaviːk/"}]</c>. Bound the same
/// way every other <c>Tts:*</c> leaf key is (a flat string under the <see cref="Section"/> section),
/// mirroring <see cref="TtsCorrectionsOptions"/> exactly — F68.5's posture (escaped, anchored,
/// timeout-bounded matching; live PUT reaches the very next render with no api restart).
///
/// Deliberately a single raw-JSON string rather than a bound <c>IList&lt;PronunciationRule&gt;</c>,
/// for the identical reason <see cref="TtsCorrectionsOptions.Corrections"/> is: the station-settings
/// overlay only expands a stored JSON array into indexed <c>IConfiguration</c> keys for arrays of
/// scalars, not arrays of objects. Parsing the JSON into a <see cref="PronunciationRuleSet"/> is
/// <see cref="PronunciationRuleProvider"/>'s job, not this class's.
/// </summary>
public sealed class TtsPronunciationsOptions
{
    public const string Section = "Tts";

    /// <summary>
    /// Raw JSON array of <c>{pattern, word, ipa}</c> rules. Null, empty, or malformed means no
    /// station pronunciation rules apply — <see cref="PronunciationRuleProvider"/> degrades to
    /// <see cref="PronunciationRuleSet.Empty"/> rather than throwing, so a typo here never breaks
    /// every subsequent render.
    /// </summary>
    public string? Pronunciations { get; init; }
}
