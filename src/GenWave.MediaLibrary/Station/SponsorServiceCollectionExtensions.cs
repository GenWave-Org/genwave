using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="ISponsorStore"/> (SPEC F171; STORY-406; PLAN T432) — <c>station.sponsor</c>
/// is a NEW domain root, not a widened existing one, so this earns its own file/class the same way
/// <see cref="ShowServiceCollectionExtensions"/> does for <c>station.show</c>, rather than folding into
/// <see cref="AdStoreServiceCollectionExtensions"/> (that class's own remarks scope it to
/// <c>station.ad_spot</c>/<c>station.ad_brief</c> specifically). Same "own connection string, own
/// <see cref="Lazy{T}"/> data source" shape every other station-schema store registration in this
/// codebase uses.
/// </summary>
public static class SponsorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISponsorStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — merely resolving <see cref="ISponsorStore"/> must
    /// never be enough to trigger a connection attempt against an empty/dev-mode connection string
    /// (the <see cref="ShowServiceCollectionExtensions.AddShowStore"/> precedent).
    /// </summary>
    public static IServiceCollection AddSponsorStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<ISponsorStore>(
            _ => new SponsorRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
