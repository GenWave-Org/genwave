namespace GenWave.Core.Domain;

/// <summary>
/// One asset <see cref="Abstractions.IJinglePackStore.UpsertAsync"/> writes as part of a jingle-pack
/// install (SPEC F165.2/F165.5, STORY-399, PLAN T414). Composes <see cref="AuthoredMediaInsert"/> —
/// the same shape every other authored <c>library.media</c> row insert builds — with the one column
/// pair that shape does not carry: <see cref="Role"/> (<c>library.media.jingle_role</c>). This seam
/// does NOT route through <see cref="Abstractions.IAuthoredCatalogWriter.InsertAuthoredAsync"/>: that
/// method is single-row, single-statement, and always lands its row in isolation, where a jingle-pack
/// install is a whole-pack upsert (drop titles the reinstalled manifest no longer names, replace the
/// rest) that <see cref="Abstractions.IJinglePackStore"/> owns end to end — see that interface's own
/// remarks for why a second write path exists rather than widening the first.
/// </summary>
/// <param name="Media">
/// Every <c>library.media</c> column this asset's row needs, already measured by the controller on
/// the STAGED file before this type is ever constructed (loudness + cue mandatory, energy optional —
/// SPEC F165.2's own enrichment contract; a jingle install fails whole rather than landing a row with
/// null cues, unlike an ordinary scan). <see cref="Media"/>'s own <c>Tags.Title</c> is the upsert key
/// this pack's install joins against together with the pack's own slug
/// (<c>library.media_pack_slug_title_key</c>, db/45) — the repository never re-derives it, this input
/// carries the one value both the SQL and this record need.
/// </param>
/// <param name="Role">
/// The manifest-declared role (<c>bed</c>/<c>sting</c>/<c>station_id</c>) this asset plays —
/// <c>library.media.jingle_role</c>'s own closed set (db/45's CHECK), already validated by
/// <c>CatalogJinglePackManifestSerializer</c> before this type is ever constructed; this seam trusts
/// its caller rather than re-validating (mirrors <see cref="VoicePackVoiceInput"/>'s own
/// "seam doesn't recompute, the caller decides" discipline).
/// </param>
public sealed record JinglePackAssetInput(AuthoredMediaInsert Media, string Role);
