namespace GenWave.Host.Options;

/// <summary>
/// Configuration for community-sourced content — currently just the Persona Catalog origin (SPEC
/// F90.1, STORY-234, PLAN T99). Bound from the <c>Community</c> config section, live via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so a PUT to
/// <c>Community:CatalogIndexUrl</c> reaches the very next catalog request with no api restart —
/// see <see cref="CommunityCatalogAccessor"/> for the read side T100/T101 consume.
/// <para>
/// <see cref="CatalogIndexUrl"/> defaults to the official <c>genwave-catalog</c> index, which will
/// be publicly reachable at F89.4 launch (it does not need to be reachable today for this default
/// to be safe): F90.4's unreachable-origin behavior is graceful — a cold cache renders a "catalog
/// unreachable" empty state, never an error — so a fresh deploy that can't yet reach the origin
/// degrades quietly rather than breaking anything.
/// <b>Empty is the F90.1 fail-closed kill switch</b>: T101's catalog endpoints 404 and the admin
/// UI hides the Persona Catalog entry point entirely — the same F87.2/F61 surface-off idiom used
/// throughout <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>.
/// </para>
/// </summary>
public sealed class CommunityOptions
{
    public const string Section = "Community";

    /// <summary>
    /// The <c>genwave-catalog</c> index.json URL the Persona Catalog proxy fetches from (SPEC
    /// F90.1). Empty disables the entire Persona Catalog surface: T101's catalog endpoints 404 and
    /// the admin UI hides the shelf.
    /// </summary>
    public string CatalogIndexUrl { get; set; } =
        "https://raw.githubusercontent.com/GenWave-Org/genwave-catalog/main/index.json";
}
