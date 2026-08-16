namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.avatar_pack_item</c> row, metadata only — NO <see cref="AvatarPackItem.Bytes"/>
/// (SPEC F128, STORY-332, PLAN T294, review finding B1). Nested inside an
/// <see cref="AvatarPackSummary"/>'s <see cref="AvatarPackSummary.Items"/> list, which
/// <see cref="Abstractions.IAvatarPackStore.GetAllAsync"/> returns for the Wardrobe Avatars tab's own
/// shelf listing — that read has no use for a raw payload, so this type is structurally incapable of
/// carrying one, mirroring <see cref="FontPackFace"/>'s own "metadata only, no bytes" split off
/// <see cref="FontPackFaceContent"/>. A caller needing an item's actual bytes reads
/// <see cref="Abstractions.IAvatarPackStore.GetBySlugAsync"/> instead, whose
/// <see cref="AvatarPack.Items"/> stays the bytes-carrying <see cref="AvatarPackItem"/> shape.
/// </summary>
/// <param name="Name">The item's name within its pack — unique per pack (the table's
/// <c>UNIQUE(pack_id, name)</c> constraint), mirrors <see cref="AvatarPackItem.Name"/>.</param>
/// <param name="SuggestedPersona">A slug hint the apply-from-pack picker highlights (T296) — an OFFER,
/// never an auto-write; <see langword="null"/> when the pack manifest names none, mirrors
/// <see cref="AvatarPackItem.SuggestedPersona"/>.</param>
public sealed record AvatarPackItemSummary(string Name, string? SuggestedPersona);
