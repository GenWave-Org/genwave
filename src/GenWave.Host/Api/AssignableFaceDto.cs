namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/fonts/assignable</c> (SPEC F104.11, STORY-286, PLAN T206; widened at T206
/// review finding F4; renamed from <c>VendoredFontDto</c>/<c>GET /api/fonts/vendored</c> at PLAN T207
/// review carry-in 1 — the old name promised "vendored only" while the route has returned vendored ∪
/// installed since T206; T207 was the last cheap moment to fix it, before any client outside this repo
/// could depend on either name) — the v2 editor's role pickers' ENTIRE assignable set, vendored ∪
/// installed, projected from <see cref="Theming.FontProvenanceCatalog.Default"/> AND
/// <see cref="GenWave.Core.Abstractions.IFontPackStore.GetAllAsync"/>. One row per FAMILY, not per file: an
/// italic file (e.g. <c>fraunces-italic-variable-latin.woff2</c>) backs the SAME family its upright
/// sibling already represents, and SPEC F104.11 offers component mix at the family level only ("a
/// face per role… no token-level colour editing" — extended here to mean no per-style/weight axis
/// either), so <see cref="Api.FontPackController.Assignable"/> resolves ONE representative face per
/// family (see that action's own remarks for the single heuristic each half uses) before this DTO is
/// ever built, rather than this type carrying a redundant "isItalic" flag nothing reads.
///
/// <para>
/// <b><see cref="Family"/> may be build-time vendored data OR an installed pack's stored
/// <c>FontPack.Family</c> — this type carries no promise about which, deliberately (T206 review
/// finding F4 widened this projection to union both).</b> The installed half is the SAME "Dean-curated
/// but STORED, catalog-sourced text" data class <c>FontLibraryPackDto.Family</c> already carries — see
/// that type's own "unbounded, don't trust it as CSS-safe on its own" remarks. What makes EITHER half
/// safe once an operator assigns it to a role is not this route or this type: it is server-side, at
/// the point a remix manifest is actually composed. Assigning a face threads <see cref="Family"/> into
/// the remix POSTed to <c>POST /api/themes/preview</c> and <c>POST /api/themes/{slug}/save-as-own</c>
/// (PLAN T207), and every one of those routes calls <see cref="Theming.ThemeManifestParser.Parse"/> —
/// whose own <c>FontFamilyPattern</c> re-validates every family a posted manifest carries, vendored or
/// installed, BEFORE <see cref="Theming.ThemeCssComposer"/> ever runs. Here, in this DTO and in the
/// Admin UI's picker, <see cref="Family"/> is plain text content only; the injection risk is closed by
/// that later re-validation, not by anything this route or this type does.
/// </para>
/// </summary>
/// <param name="Family">The CSS family name a picked face's role composes under, e.g. "Fraunces".</param>
/// <param name="Src">The <c>/fonts/&lt;file&gt;</c> path the editor's remix manifest asset resolves to
/// — <see cref="Theming.VendoredFontFace.Src"/> verbatim for the vendored half, the SAME
/// <c>/fonts/{file}</c> shape built from an installed pack's own representative
/// <c>FontPackFace.File</c> for the installed half (the exact template
/// <see cref="Theming.VendoredFontFace.Src"/> itself uses, so both halves share one string shape even
/// though only one literally reuses the property).</param>
public sealed record AssignableFaceDto(string Family, string Src);
