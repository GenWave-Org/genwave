using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IScheduleSpecialStore"/> (SPEC F120.1, STORY-317, PLAN T258). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: <c>station.
/// schedule_special</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not <c>library</c>
/// — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="ScheduleServiceCollectionExtensions"/>'s and <see cref="ShowServiceCollectionExtensions"/>'s
/// registrations use.
///
/// T258 ships this registration deliberately without a Host call site (mirrors
/// <see cref="ShowServiceCollectionExtensions.AddShowStore"/>'s own original T239 shape — "no consumer
/// lands with this seam"): PLAN T259's dated-list-form API is the first consumer.
/// </summary>
public static class SpecialsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IScheduleSpecialStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/> — singleton
    /// lifetime is load-bearing here for the same reason
    /// <see cref="ScheduleServiceCollectionExtensions.AddScheduleStore"/>'s own remarks give:
    /// <see cref="SpecialsRepository"/>'s <see cref="IScheduleSpecialStore.SpecialsChanged"/> event only
    /// means anything if every caller shares the SAME instance for the life of the process. The data
    /// source build is wrapped in a <see cref="Lazy{T}"/> — merely resolving
    /// <see cref="IScheduleSpecialStore"/> must never be enough to trigger a connection attempt against
    /// an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddScheduleSpecialStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IScheduleSpecialStore>(
            _ => new SpecialsRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
