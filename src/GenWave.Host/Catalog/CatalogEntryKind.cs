namespace GenWave.Host.Catalog;

/// <summary>
/// The kind of content one catalog index entry carries (SPEC F103.1) — the discriminator that
/// admits a second entry kind onto the SAME fetch/verify/cache/auth machinery the persona-only
/// catalog (F89–F90) already ships, with every further kind (font/icon/avatar, F103.14) landing
/// the same additive way. <see cref="CatalogIndexValidator"/> is the only place a raw index.json
/// <c>kind</c> string is ever resolved into this: a missing field defaults to <see cref="Persona"/>
/// (back-compat for every entry authored before this field existed), and a value naming neither
/// case here is treated as forward-compat — the WHOLE entry is skipped rather than parsed against
/// either shape — deliberately unlike an unrecognised <c>audience</c>, which still rejects the
/// whole index (audience is content-safety; kind is forward-compat, and the two must not be
/// conflated).
/// </summary>
public enum CatalogEntryKind
{
    Persona,
    Theme,
}
