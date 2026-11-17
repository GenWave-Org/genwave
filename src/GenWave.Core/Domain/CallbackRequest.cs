namespace GenWave.Core.Domain;

/// <summary>
/// What the planner needs to decide on one break (SPEC F218.41, F218.45).
/// </summary>
/// <param name="PersonaId">The speaking DJ.</param>
/// <param name="Kind">The break's segment kind; only LeadIn and BackAnnounce can carry a callback (F218.40).</param>
/// <param name="CallbacksPerHour"><c>Memory:CallbacksPerHour</c>; 0 or less is off, above 6 counts as 6.</param>
/// <param name="ExclusionTexts">Texts whose words never count as a use: every DJ's name, the station name, this
/// break's track/artist/album, the DJ's card text (never lore).</param>
public sealed record CallbackRequest(long PersonaId, SegmentKind Kind, int CallbacksPerHour, IReadOnlyList<string?> ExclusionTexts);
