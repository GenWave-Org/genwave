namespace GenWave.Host.Api;

/// <summary>
/// One credit line (SPEC F169.2, STORY-404, PLAN T419). <see cref="Creator"/>/<see cref="SourceUrl"/>
/// are <see langword="null"/> for a CC0 aggregate line, a font-pack line (no per-source creator
/// recorded), and a voice-pack's synthetic-blend line — never for a CC-BY jingle asset line, whose
/// <see cref="License"/> is always <c>"CC-BY"</c>.
/// </summary>
/// <param name="Title">A CC-BY asset's own title; the pack's own display name for every other line
/// kind (a CC0 aggregate, a font pack, or a voice pack's synthetic-blend line).</param>
/// <param name="Creator">The credited creator, or <see langword="null"/> when this line carries none.</param>
/// <param name="SourceUrl">Where the asset came from, or <see langword="null"/> when this line carries
/// none.</param>
/// <param name="License">The line's licence token: <c>"CC-BY"</c>, <c>"CC0"</c>, a font pack's own
/// stored licence string, or the voice-pack constant <c>"Synthetic blend"</c>.</param>
public sealed record AttributionLineDto(string Title, string? Creator, string? SourceUrl, string License);
