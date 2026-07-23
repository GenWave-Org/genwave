using System.Text.Json.Serialization;

namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/booth-log</c> (SPEC F72.2): an operator-readable narrative entry.
/// <see cref="Id"/> (SPEC F84.1, F84.6; STORY-215, PLAN T71) is the row's own DB id — the wire's
/// airing identity, and the <c>POST /api/booth-log/{id}/taste-thumb</c> target for this row
/// directly. <see cref="PersonaId"/> (SPEC F84.6, STORY-215) is the persona stamped on air for a
/// track-start row — <see langword="null"/> for every other kind, a persona-less airing, or a row
/// that predates the column. The admin UI (T71) uses <see cref="PersonaId"/>'s presence to decide
/// whether a row is thumbable for taste, and <see cref="Id"/> as the thumb's POST target.
///
/// <see cref="Pick"/> (SPEC F86.2, STORY-217, PLAN T74) mirrors the row's stored
/// <see cref="GenWave.Core.Domain.BoothLogPickStamp"/> — fired-rule summaries, signed weights, and
/// the exploration flag. <c>JsonIgnore(WhenWritingNull)</c> makes it ABSENT from the JSON, not
/// null-valued, for a row whose stored pick is null (engine-initiated play, persona-off pick, or a
/// row predating the column) — same discipline <see cref="GenWave.Core.Domain.PersonaCard.Taste"/>
/// already established for an optional collection field.
///
/// <see cref="TasteExcluded"/> (gh-#99) is true for a track-start row whose stamped media id lives
/// in a <c>Station:SafeScope:LibraryIds</c> library — safe-loop tracks and station IDs. The admin
/// UI renders NO taste thumbs for such a row (regardless of <see cref="PersonaId"/>), and the thumb
/// endpoint independently refuses it. Computed at read time against the live safe scope, so a
/// SafeScope edit governs the very next page.
/// </summary>
public sealed record BoothLogEntryDto(
    long Id,
    DateTime OccurredAt,
    string Kind,
    string Summary,
    long? PersonaId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BoothLogPickDto? Pick = null,
    bool TasteExcluded = false);
