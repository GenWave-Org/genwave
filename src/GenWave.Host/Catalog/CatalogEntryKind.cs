namespace GenWave.Host.Catalog;

/// <summary>
/// The kind of content one catalog index entry carries (SPEC F103.1, widened to <see cref="Font"/>
/// by F104.1) — the discriminator that admits a further entry kind onto the SAME
/// fetch/verify/cache/auth machinery the persona-only catalog (F89–F90) already ships, with every
/// STILL-further kind (icon/avatar, F103.14) landing the same additive way.
/// <see cref="CatalogIndexValidator"/> is the only place a raw index.json <c>kind</c> string is
/// ever resolved into this: a missing field defaults to <see cref="Persona"/> (back-compat for
/// every entry authored before this field existed), and a value naming none of the cases here is
/// treated as forward-compat — the WHOLE entry is skipped rather than parsed against any shape —
/// deliberately unlike an unrecognised <c>audience</c>, which still rejects the whole index
/// (audience is content-safety; kind is forward-compat, and the two must not be conflated).
/// </summary>
public enum CatalogEntryKind
{
    Persona,
    Theme,

    /// <summary>
    /// A curated font pack (SPEC F104.1) — the first entry kind whose manifest is joined by
    /// <c>assets[]</c>: 1–2 latin-subsetted woff2 faces plus the pack's OFL licence text, each
    /// riding the index with its own <c>path</c>/<c>sha256</c>/<c>bytes</c>
    /// (<see cref="CatalogAssetRef"/>).
    /// </summary>
    Font,

    /// <summary>
    /// A named show (SPEC F118.1, PLAN T254) — the same minimal <c>{manifest, meta}</c> shape a theme
    /// entry carries, minus a theme's own <c>preview</c>/<c>family</c>/<c>assets</c> (a show has none
    /// of those): manifest <c>&lt;slug&gt;.show.json</c> (schema version, name, tagline, flavor); meta
    /// carries the usual author/description/audience/added/bestFor plus the show-specific, OPTIONAL
    /// <c>suggestedPersona</c> field (SPEC F118.3) — never projected onto the index (unlike
    /// <c>bestFor</c>), since it is only ever consulted once, at import time
    /// (<see cref="Api.CatalogController"/>'s own <c>ToEntryResponse</c>).
    /// </summary>
    Show,
}
