namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of one <c>station.persona_taste</c> row (mapped by the globally-enabled
/// <c>MatchNamesWithUnderscores</c>, same as <see cref="Catalog.MediaRow"/>). <see cref="Predicate"/>
/// and <see cref="Context"/> arrive as their <c>::text</c>-cast jsonb so
/// <see cref="PersonaTasteRepository"/> can deserialize them into
/// <see cref="GenWave.Core.Domain.TastePredicate"/>/<see cref="GenWave.Core.Domain.TasteContext"/>
/// itself — Dapper has no built-in jsonb-to-nested-record mapping for a shape like
/// <see cref="GenWave.Core.Domain.PersonaTasteEntry"/>. <see cref="Weight"/> is <see langword="float"/>,
/// matching the column's <c>real</c> storage width exactly (widened to <see langword="double"/> only
/// when <see cref="PersonaTasteRepository"/> builds the domain-facing <see cref="GenWave.Core.Domain.TasteRule"/>).
/// </summary>
sealed record PersonaTasteRow(
    long Id,
    long PersonaId,
    string Predicate,
    string Context,
    float Weight,
    string Source,
    DateTime CreatedAt,
    DateTime UpdatedAt);
