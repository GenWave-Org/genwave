namespace GenWave.Core.Domain;

/// <summary>
/// Provenance of a <c>station.persona_avatar</c> row (SPEC F128-F129, STORY-333, PLAN T290) — the
/// CHECK-constrained <c>source</c> column's C# projection, mirroring
/// <see cref="PersonaMemorySource"/>'s own enum-over-text-CHECK convention. <see cref="Upload"/> is a
/// direct owner upload through the image-normalize pipeline (T291); <see cref="Catalog"/> is a face
/// copied in from an installed <see cref="AvatarPack"/> item or a persona entry's own sidecar asset —
/// either way, "assignment copies, provenance records" (ARCHITECTURE.md's own ruling): the bytes are
/// this row's own, never a live reference back to the pack/entry they came from.
/// </summary>
public enum PersonaAvatarSource
{
    Upload,
    Catalog,
}
