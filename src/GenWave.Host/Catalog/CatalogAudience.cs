namespace GenWave.Host.Catalog;

/// <summary>
/// A persona catalog entry's content self-rating (SPEC F89.3, F90.2) — index.json's own
/// <c>audience</c> field, always exactly <c>"everyone"</c> or <c>"mature"</c>
/// (genwave-catalog's schemas/index.schema.json enum). Any other raw value fails
/// <see cref="CatalogIndexValidator"/>'s strict shape check and rejects the WHOLE index (F90.2),
/// never just the one offending entry — an unrecognized rating is a build regression in the
/// catalog tooling, not something this station can safely guess at.
/// </summary>
public enum CatalogAudience
{
    Everyone,
    Mature,
}
