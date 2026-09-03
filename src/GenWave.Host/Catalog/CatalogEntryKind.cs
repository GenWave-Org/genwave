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

    /// <summary>
    /// An avatar pack (SPEC F128.1, PLAN T292) — the SECOND assets-carrying kind, alongside
    /// <see cref="Font"/>: manifest <c>&lt;slug&gt;.avatar.json</c> (pack name + <c>items[{name, file,
    /// suggestedPersona?}]</c>) + the usual meta, with one PNG per item riding <c>assets[]</c> (the
    /// F104 <c>{path, sha256, bytes}</c> shape) — a pack IS its files, same all-or-nothing posture
    /// <see cref="Font"/> already has (<see cref="CatalogIndexValidator.TryValidateAssets"/>). Deep PNG
    /// re-validation (magic bytes, IHDR 512², acTL reject) happens at INSTALL time (PLAN T293) —
    /// <see cref="CatalogIndexValidator"/> only checks the index/manifest SHAPE here, never bytes.
    /// </summary>
    Avatar,

    /// <summary>
    /// An icon pack (SPEC F130.6, PLAN T292) — manifest <c>&lt;slug&gt;.icon.json</c> + the usual
    /// meta, carrying NO binary <c>assets[]</c> at all (the constrained-vector-JSON pack body lives
    /// entirely inside the manifest document itself, SPEC F130.1) — the same minimal
    /// <c>{manifest, meta}</c> shape <see cref="Show"/> already has.
    /// </summary>
    Icon,

    /// <summary>
    /// An ad-pack (SPEC F162.2, STORY-393, PLAN T405) — manifest <c>&lt;slug&gt;.ad-pack.json</c> +
    /// the usual meta, DATA ONLY: pack metadata plus <c>briefs[]</c> (brand/premise/tone/structure,
    /// <see cref="CatalogAdPackBrief"/>), the same minimal <c>{manifest, meta}</c> shape
    /// <see cref="Icon"/> already has — no binary <c>assets[]</c> at all, and (unlike every other
    /// pack-shaped kind) no script/audio/code of any kind ever crosses this trust boundary. Install
    /// (<see cref="Api.AdPackController"/>) upserts each declared brief into
    /// <c>station.ad_brief</c>, keyed <c>(pack_slug, brand)</c> — a DURABLE write no other catalog
    /// kind's install route performs; every installed brief still faces SPEC F160.3's
    /// <c>AdScriptValidator</c> at generation time, exactly like an owner-authored one.
    /// </summary>
    AdPack,
}
