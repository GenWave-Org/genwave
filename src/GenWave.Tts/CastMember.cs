namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// One member of a <see cref="CastAssemblyRequest.Cast"/> (SPEC F161.2, STORY-391, PLAN T401 review
/// F8) — pairs a tag with the bare <see cref="VoiceSpec"/> that tag renders with. A named record
/// rather than a raw <c>(string Tag, VoiceSpec Voice)</c> tuple, consistent with <see cref="CastLine"/>'s
/// own shape immediately beside it — two "(Tag, X)" pairs in the same request should not be one
/// named type and one anonymous one.
/// </summary>
/// <param name="Tag">Which cast member this is — must match at least one <see cref="CastLine.Tag"/>
/// for this voice to ever render (see <see cref="CrosstalkAssembler.AssembleCastAsync"/>'s own
/// fail-fast validation).</param>
/// <param name="Voice">The bare voice this tag renders with — never a persona card (SPEC F161.2:
/// ad voices are actors, not the station's DJs).</param>
public sealed record CastMember(string Tag, VoiceSpec Voice);
