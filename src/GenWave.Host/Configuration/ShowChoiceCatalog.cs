using GenWave.Core.Abstractions;

namespace GenWave.Host.Configuration;

/// <summary>
/// Live <see cref="IChoiceCatalog"/> for <c>Crosstalk:Shows</c> (SPEC F205.7, STORY-482, PLAN T585) —
/// wraps <see cref="IShowStore.GetAllAsync"/>, mapping each row's <see cref="Core.Domain.Show.Slug"/>/
/// <see cref="Core.Domain.Show.Name"/> onto a <see cref="SettingChoice"/> pair (T175's own "names
/// slugs, not labels" rule: the stored value is the slug, the display label is the name). Ordered
/// exactly as <see cref="IShowStore.GetAllAsync"/> itself returns rows — that seam's own contract is
/// already "ordered by name," so this catalog does not re-sort. <see cref="IShowStore"/> arrives via
/// plain constructor injection, never <see cref="Lazy{T}"/>: unlike <see cref="LlmModelChoiceProbe"/>'s
/// own typed <c>HttpClient</c>, resolving the registered <see cref="IShowStore"/> singleton is already
/// side-effect free (its own <c>Lazy&lt;NpgsqlDataSource&gt;</c> defers the actual connection build to
/// first use) — <see cref="ChoiceSourceBootCheck"/> resolving this catalog at boot to read
/// <see cref="Kind"/> therefore never touches the database either.
/// </summary>
internal sealed class ShowChoiceCatalog(IShowStore showStore) : IChoiceCatalog
{
    /// <summary>The <see cref="StationSettingsAllowlist"/> <c>Crosstalk:Shows</c> entry's own
    /// <see cref="SettingChoiceSource.Catalog.Kind"/> — also what <see cref="ChoiceSourceBootCheck"/>
    /// checks the allowlist against.</summary>
    public const string CatalogKind = "shows";

    public string Kind => CatalogKind;

    public async Task<IReadOnlyList<SettingChoice>> ListAsync(CancellationToken ct)
    {
        var shows = await showStore.GetAllAsync(ct).ConfigureAwait(false);
        return shows.Select(show => new SettingChoice(show.Slug, show.Name)).ToList();
    }
}
