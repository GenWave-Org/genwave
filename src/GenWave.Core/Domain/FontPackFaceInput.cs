namespace GenWave.Core.Domain;

/// <summary>
/// One face <see cref="Abstractions.IFontPackStore.UpsertAsync"/> writes as part of a pack install
/// (SPEC F104, STORY-282, PLAN T198) — the write-side counterpart to the read-side
/// <see cref="FontPackFace"/>, carrying the actual payload a caller (the future
/// <c>FontPackController</c> install route, T199) already fetched and hash-verified through the
/// guarded-door transport (<c>CatalogProxyService.GetAssetAsync</c>) before this seam ever sees it.
/// </summary>
/// <param name="File">The <c>/fonts/&lt;file&gt;</c> basename this face installs under — see
/// <see cref="FontPackFace.File"/>'s own remarks.</param>
/// <param name="Bytes">The face's raw payload — the exact bytes <c>station.font_pack_face.bytes</c>
/// stores.</param>
/// <param name="Sha256">The payload's hash, PINNED from the catalog index's own already-verified
/// asset hash (SPEC F104's own "pinned at install from the index" DDL comment) rather than
/// recomputed here — the store persists whatever the caller already trusts, the same
/// "seam doesn't recompute, the caller decides" discipline
/// <c>PersonaImportRequest.ImportedFrom</c> follows for provenance. <see cref="ByteSize"/>, by
/// contrast, is NEVER caller-supplied — see its own remarks for why.</param>
/// <param name="Style">CSS font-style — see <see cref="FontPackFace.Style"/>'s own remarks. Defaults
/// to <see cref="NormalStyle"/>, mirroring <c>station.font_pack_face.style</c>'s own column
/// default.</param>
public sealed record FontPackFaceInput(string File, byte[] Bytes, string Sha256, string Style = FontPackFaceInput.NormalStyle)
{
    /// <summary>The default CSS font-style (SPEC F104's DDL default) — hoisted so
    /// <see cref="Style"/>'s own default and every caller/test asserting on it reference the same
    /// constant rather than independently-typed copies of the string.</summary>
    public const string NormalStyle = "normal";

    /// <summary>
    /// The payload's byte count, ALWAYS derived from <see cref="Bytes"/>.<c>Length</c> rather than a
    /// separate constructor parameter — unlike <see cref="Sha256"/> (a provenance stamp pinned from
    /// elsewhere), a byte count has no meaning independent of the bytes it describes, so accepting it
    /// as a second, independently-settable value would admit an illegal state (a
    /// <see cref="FontPackFaceInput"/> whose declared size disagrees with its own payload) that this
    /// property makes impossible to construct.
    /// </summary>
    public int ByteSize => Bytes.Length;
}
