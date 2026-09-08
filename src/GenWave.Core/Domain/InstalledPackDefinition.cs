namespace GenWave.Core.Domain;

/// <summary>
/// One row from a listing of installed packs (SPEC F169.2, STORY-404, PLAN T419) — the slug and raw
/// <c>definition</c> jsonb text of a single <c>station.jingle_pack</c> or <c>station.voice_pack</c> row,
/// nothing else. Deliberately as thin as <see cref="FontPack"/>'s own <c>Definition</c> field: the
/// caller (today, only <c>GenWave.Host.Api.AttributionsController</c>) reconstitutes the pack's own
/// Host-side manifest type at its own edge — this record carries no opinion about what that manifest
/// looks like, mirroring <see cref="GenWave.Core.Abstractions.IJinglePackStore"/>'s and
/// <see cref="GenWave.Core.Abstractions.IVoicePackStore"/>'s shared "opaque jsonb text in Core"
/// discipline.
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack of that kind.</param>
/// <param name="DefinitionJson">The stored <c>definition</c> column, verbatim jsonb text.</param>
public sealed record InstalledPackDefinition(string Slug, string DefinitionJson);
