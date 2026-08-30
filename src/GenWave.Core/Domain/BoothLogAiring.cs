namespace GenWave.Core.Domain;

/// <summary>
/// Booth-log row <c>id</c>'s own kind/media/instant (SPEC F150.8, STORY-370, PLAN T367) — what
/// <see cref="Abstractions.IBoothLogReader.GetTrackAiringAsync"/> reads back for the station-thumb
/// action. Deliberately carries <see cref="Kind"/> even when it is not <c>"track-started"</c> — the
/// action's own 400 must NAME the row's kind (F150.8: "non-music rows are not thumbable, 400 naming
/// the kind"), so a collapsed <see langword="null"/> (the way <see cref="Abstractions.IBoothLogReader.GetMediaIdAsync"/>
/// answers "no", uninformatively, for a missing row, a non-track row, or a row predating the column)
/// would lose exactly the value the caller needs. <see langword="null"/> is reserved for "row
/// <c>id</c> does not exist at all" (404) — the one case this type cannot itself represent, since
/// there is no kind to report.
/// </summary>
public sealed record BoothLogAiring(string Kind, long? MediaId, DateTimeOffset OccurredAt);
