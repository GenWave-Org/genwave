namespace GenWave.Host.Api;

/// <summary>
/// One installed pack's credits (SPEC F169.2, STORY-404, PLAN T419) — the pack's own identity plus
/// every credit line <see cref="AttributionsController"/> projected off its stored manifest. What
/// counts as a "line" is kind-specific: see <see cref="AttributionsController"/>'s own remarks for
/// the jingle (per-CC-BY-asset plus one CC0 aggregate), font (one pack-level line), and voice pack
/// (one synthetic-blend line) projections.
/// </summary>
/// <param name="Slug">The catalog entry's own slug this pack installed from.</param>
/// <param name="Name">The pack's display name — a jingle/voice pack's <c>packName</c>, or a font
/// pack's <c>family</c>.</param>
/// <param name="Attributions">This pack's credit lines, in the order <see cref="AttributionsController"/>
/// projected them.</param>
public sealed record AttributionPackDto(
    string Slug, string Name, IReadOnlyList<AttributionLineDto> Attributions);
