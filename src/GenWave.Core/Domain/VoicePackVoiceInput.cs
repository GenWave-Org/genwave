namespace GenWave.Core.Domain;

/// <summary>
/// One voice <see cref="Abstractions.IVoicePackStore.UpsertAsync"/> writes as part of a voice-pack
/// install (SPEC F164.5, STORY-395, PLAN T413). Unlike <see cref="FontPackFaceInput"/>'s own payload,
/// the <c>.pt</c> bytes never pass through this seam — they are written straight to
/// <c>Packs:VoicesRoot</c> by the controller BEFORE the database write begins (the brief's own
/// "write files first, then the transaction" ordering), so this input carries only the metadata
/// <c>station.voice_pack_voice</c> actually persists.
/// </summary>
/// <param name="VoiceId">The kokoro voice id this row names (e.g. <c>af_nova</c>) — already validated
/// against <c>SettingValidator.VoiceIdFormat()</c> plus the install-time 64-character cap by
/// <c>CatalogVoicePackManifestSerializer</c> before this type is ever constructed; this seam trusts
/// its caller rather than re-validating (mirrors <see cref="FontPackFaceInput"/>'s own
/// "seam doesn't recompute, the caller decides" discipline).</param>
/// <param name="File">The RELATIVE file name the voice was written under, always
/// <c>VoiceId + ".pt"</c> (PLAN T412's flat-layout ruling, ARCHITECTURE.md:3327/3520-3523) — never a
/// path, so relocating <c>Packs:VoicesRoot</c> never breaks a stored row.</param>
/// <param name="GenderHint">Optional manifest-declared gender (<c>female</c>/<c>male</c>/<c>neutral</c>),
/// or <see langword="null"/> if the manifest omitted it.</param>
/// <param name="AgeHint">Optional manifest-declared age band (<c>child</c>/<c>young</c>/<c>adult</c>/
/// <c>senior</c>), or <see langword="null"/> if the manifest omitted it.</param>
/// <param name="PreviewSha">The sha256 of the pack's shared preview clip (SPEC F164.4), verified by the
/// controller's fetch but never written to disk (the shelf plays it straight from the catalog) —
/// recorded here purely for provenance, same value on every voice row a given install writes.</param>
public sealed record VoicePackVoiceInput(string VoiceId, string File, string? GenderHint, string? AgeHint, string? PreviewSha);
