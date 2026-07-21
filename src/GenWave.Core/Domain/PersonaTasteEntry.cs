namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.persona_taste</c> (SPEC F82.1; STORY-213) —
/// <see cref="Abstractions.IPersonaTasteStore"/>'s read projection. <see cref="Rule"/> carries the
/// same <see cref="TasteRule"/> shape a <see cref="PersonaCard"/> exports (predicate/context/weight);
/// <see cref="Source"/> and the row's own identity/timestamps are storage-only metadata a card never
/// carries (a card only ever holds <c>source='authored'</c> rules, and never their database id).
/// </summary>
public sealed record PersonaTasteEntry(
    long Id,
    long PersonaId,
    TasteRule Rule,
    PersonaTasteSource Source,
    DateTime CreatedAt,
    DateTime UpdatedAt);
