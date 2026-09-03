using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IAdSpotStore"/>/<see cref="IAdBriefStore"/> (SPEC F159.1, F159.2, F162.2;
/// STORY-389; PLAN T398) — named for the domain both registrations share (the
/// <see cref="PersonaServiceCollectionExtensions"/> precedent: one class per domain, not one per
/// store, once a domain grows past its first store). Deliberately separate from
/// <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.ad_spot</c>/<c>station.ad_brief</c> live in the <c>station</c> schema/role
/// (<c>station_svc</c>), not <c>library</c> — the same "own connection string, own
/// <see cref="Lazy{T}"/> data source" shape <see cref="AnnouncementServiceCollectionExtensions"/>'s
/// own registration uses.
///
/// Two separate registrations, each over its own <see cref="NpgsqlDataSource"/> (the
/// <see cref="PersonaServiceCollectionExtensions.AddPersonaImportStore"/> precedent: "each store
/// builds its own lazy data source... one extra idle pool, not one extra live connection") rather
/// than one repository spanning both tables — <see cref="AdSpotRepository"/>'s own state machine and
/// <see cref="AdBriefRepository"/>'s own plain upsert are different enough concerns (SRP) to earn
/// separate types, matching PLAN T398's own design line ("<c>IAdSpotStore</c>/<c>AdSpotRepository</c>
/// ... + <c>IAdBriefStore</c>").
/// </summary>
public static class AdStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAdSpotStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="AnnouncementServiceCollectionExtensions.AddAnnouncementStore"/>'s own remarks:
    /// merely resolving <see cref="IAdSpotStore"/> must never be enough to trigger a connection
    /// attempt against an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddAdSpotStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IAdSpotStore>(
            _ => new AdSpotRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));

    /// <summary>
    /// Registers <see cref="IAdBriefStore"/> the same lazy way <see cref="AddAdSpotStore"/> registers
    /// <see cref="IAdSpotStore"/>, over the same <paramref name="connectionString"/> — a SEPARATE
    /// <see cref="NpgsqlDataSource"/> instance (see this class's own remarks for why).
    /// </summary>
    public static IServiceCollection AddAdBriefStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IAdBriefStore>(
            _ => new AdBriefRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
