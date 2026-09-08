namespace GenWave.Host.Api;

/// <summary>
/// One populated pack kind on <see cref="AttributionsResponse.Groups"/> (SPEC F169.2, STORY-404,
/// PLAN T419) — every installed pack of that kind, ordered by slug.
/// </summary>
/// <param name="Kind">The catalog kind token this group covers: <c>jingle-pack</c>, <c>font-pack</c>,
/// or <c>voice-pack</c> — the same tokens the Community Catalog itself uses.</param>
/// <param name="Packs">Every installed pack of this kind, ordered by <see cref="AttributionPackDto.Slug"/>.</param>
public sealed record AttributionGroupDto(string Kind, IReadOnlyList<AttributionPackDto> Packs);
