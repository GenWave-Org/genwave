namespace GenWave.Host.Catalog;

/// <summary>
/// Which half of a catalog entry a <see cref="CatalogEntryFetchResult"/> failure names (SPEC
/// F90.3). <see cref="Manifest"/> is the entry's primary document — a persona's <c>.persona.json</c>
/// card today, a theme's <c>.theme.json</c> once that kind exists (SPEC F103.2's generalisation).
/// </summary>
public enum CatalogEntryFilePart
{
    Manifest,
    Meta,
}
