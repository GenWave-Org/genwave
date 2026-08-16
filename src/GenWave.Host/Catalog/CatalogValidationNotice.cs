namespace GenWave.Host.Catalog;

/// <summary>
/// One non-fatal issue <see cref="CatalogIndexValidator.TryValidate"/> surfaced while validating a
/// single entry (round-1 review findings 1/3, PLAN T292). <see cref="CatalogIndexValidator"/> stays
/// pure and log-free by design (its own class remarks — "no HTTP, no caching, no logging");
/// <see cref="CatalogProxyService"/> is the one caller that turns each of these into the WARN log
/// line SPEC F90.3's own per-entry "withheld" shape already established (mirrors
/// <c>CatalogProxyService.WithheldHashMismatch</c>/<c>WithheldOversize</c>'s own
/// <c>"slug={Slug} ..."</c> shape). <see cref="Slug"/> and <see cref="Reason"/> are already
/// human-readable — the same style of text a whole-index <see cref="CatalogIndexValidator.TryValidate"/>
/// rejection reason already carries — so the logging call site never re-derives or re-formats
/// anything this class already worked out.
/// </summary>
internal sealed record CatalogValidationNotice(string Slug, CatalogValidationNoticeKind Kind, string Reason);
