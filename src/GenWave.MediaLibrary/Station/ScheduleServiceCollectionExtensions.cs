using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IScheduleStore"/> (SPEC F91.1, STORY-240, STORY-242, PLAN T118).
/// Deliberately separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.segment_schedule</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="PersonaServiceCollectionExtensions"/>'s registrations use.
///
/// T118 ships this registration deliberately without a Host call site (mirrors
/// <see cref="PersonaServiceCollectionExtensions.AddPersonaTasteStore"/>'s own original shape): the
/// <c>ScheduleResolver</c> (T119) and the <c>GET/PUT /api/schedule</c> endpoint (T122) are the first
/// consumers, landing in later tasks.
/// </summary>
public static class ScheduleServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IScheduleStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/> — singleton
    /// lifetime is load-bearing here, not just convention: <see cref="ScheduleRepository"/>'s
    /// <see cref="IScheduleStore.WeekChanged"/> event only means anything if every caller shares the
    /// SAME instance for the life of the process. The data source build is wrapped in a
    /// <see cref="Lazy{T}"/> — mirrors
    /// <see cref="PersonaServiceCollectionExtensions.AddPersonaStore"/>'s own remarks: merely
    /// resolving <see cref="IScheduleStore"/> must never be enough to trigger a connection attempt
    /// against an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddScheduleStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IScheduleStore>(
            sp => new ScheduleRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
                sp.GetRequiredService<ILogger<ScheduleRepository>>()));
}
