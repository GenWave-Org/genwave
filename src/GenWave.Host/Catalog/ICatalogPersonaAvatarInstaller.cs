namespace GenWave.Host.Catalog;

/// <summary>
/// The seam <see cref="Api.PersonaController.Import"/> calls after a catalog-origin import commits
/// (SPEC F128.7, STORY-334, PLAN T297) — extracted purely so <c>PersonaController</c>'s OWN existing
/// direct-construction unit tests (Story120_PersonaEndpoints.cs, Story123_PreviewEndpoints.cs,
/// Story219_PersonaTasteInspector.cs — none of which exercise <c>Import</c> at all) can hand it a
/// throwing stub rather than assembling <see cref="CatalogPersonaAvatarInstaller"/>'s own real
/// dependency graph (<see cref="CatalogProxyService"/>, <see cref="Images.ImageNormalizeService"/>)
/// just to satisfy a constructor parameter those tests never invoke — mirrors every OTHER
/// <c>PersonaController</c> dependency already being interface-shaped for exactly this reason. See
/// <see cref="CatalogPersonaAvatarInstaller"/>'s own remarks for the one implementation's full
/// contract and its THE FACE IS DECORATIVE posture.
/// </summary>
public interface ICatalogPersonaAvatarInstaller
{
    /// <inheritdoc cref="CatalogPersonaAvatarInstaller.InstallIfPresentAsync"/>
    Task InstallIfPresentAsync(long personaId, string catalogSlug, CancellationToken ct);
}
