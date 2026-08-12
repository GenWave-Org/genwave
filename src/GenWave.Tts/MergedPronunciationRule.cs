namespace GenWave.Tts;

/// <summary>
/// One row of <see cref="PronunciationRuleSet.MergeWithProvenance"/>'s projection (SPEC F97.3, F97.4;
/// T144's rules API): a compiled <see cref="PronunciationRule"/> alongside <see cref="Source"/> — the
/// side of the F97.3 merge that supplied it — and <see cref="InEffect"/> — whether it is the one
/// actually firing after the persona-over-station precedence flip (F97.4). A SHADOWED station row
/// (<see cref="InEffect"/> <see langword="false"/>) still appears here, unlike
/// <see cref="PronunciationRuleSet.Merge"/>'s own output, which drops it entirely — this type exists
/// so an operator-facing list can show a shadowed rule rather than have it silently vanish.
///
/// Deliberately a sibling type, not a widened <see cref="PronunciationRule"/> — <see cref="Source"/>
/// and <see cref="InEffect"/> are facts about ONE READ's merge, never properties of the rule's own
/// stored data (T144 review guidance).
/// </summary>
public sealed record MergedPronunciationRule(PronunciationRule Rule, PronunciationRuleSource Source, bool InEffect);
