namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.font_pack_face</c> row, metadata only — no <c>bytes</c> (SPEC F104, STORY-282,
/// PLAN T198). Nested inside a <see cref="FontPack"/>'s <see cref="FontPack.Faces"/> list, which
/// <see cref="Abstractions.IFontPackStore.GetAllAsync"/> returns for the library page and
/// <c>InstalledFontCatalog</c> — neither needs a face's raw payload, so this deliberately excludes
/// it; <see cref="Abstractions.IFontPackStore.GetFaceByFileAsync"/> is the seam that returns bytes,
/// via <see cref="FontPackFaceContent"/> instead.
/// </summary>
/// <param name="File">The <c>/fonts/&lt;file&gt;</c> basename — the serving key the widened
/// <c>/fonts/{file}</c> route (T200) looks this face up by; unique across every installed pack (the
/// table's <c>UNIQUE(file)</c> constraint).</param>
/// <param name="Style">CSS font-style this face renders as — <c>"normal"</c> or <c>"italic"</c>
/// (SPEC F104's own two-value comment; no CHECK constraint at the store, mirroring
/// <c>station.theme</c>'s own no-embellishment precedent).</param>
/// <param name="ByteSize">The stored payload's byte count, recorded at install time.</param>
/// <param name="Sha256">The stored payload's hash, pinned at install from the catalog index's own
/// verified asset hash — lowercase hex, matching <c>Convert.ToHexStringLower</c>'s output
/// (<c>CatalogProxyService</c>'s own hash-verification convention).</param>
public sealed record FontPackFace(string File, string Style, int ByteSize, string Sha256);
