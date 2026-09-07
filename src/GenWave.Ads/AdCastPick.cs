namespace GenWave.Ads;

/// <summary>
/// The result of <see cref="AdCastPicker.Pick"/> — the three <see cref="AdVoicePlanEntry"/> rows
/// (ANNOUNCER, VOICE1, VOICE2, always all three, never fewer) plus the <see cref="AdCastOutcome"/> the
/// worker needs to decide whether to log (SPEC F167.2-F167.4; STORY-402; PLAN T415).
/// </summary>
/// <param name="Entries">Always exactly three entries, one per tag, in ANNOUNCER/VOICE1/VOICE2 order.</param>
/// <param name="Outcome">Which branch of F167.2-F167.4 produced <paramref name="Entries"/>.</param>
internal sealed record AdCastPick(IReadOnlyList<AdVoicePlanEntry> Entries, AdCastOutcome Outcome);
