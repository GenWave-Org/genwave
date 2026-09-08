using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IVoicePackStore"/> (SPEC F164.5/F164.6/F166.4, STORY-395/396/398/401,
/// PLAN T413). Mirrors <see cref="FontPackServiceCollectionExtensions"/>'s own shape exactly:
/// <c>station.voice_pack</c>(+<c>_voice</c>) lives in the <c>station</c> schema/role
/// (<c>station_svc</c>), its own connection string, its own <see cref="Lazy{T}"/> data source.
/// </summary>
public static class VoicePackServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IVoicePackStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>, wrapped in a
    /// <see cref="Lazy{T}"/> — merely resolving <see cref="IVoicePackStore"/> must never be enough to
    /// trigger a connection attempt against an empty/dev-mode connection string.
    /// <see cref="VoicePackRepository"/> is hand-constructed rather than DI-activated (it needs this
    /// method's own <paramref name="connectionString"/>, not the default <c>library_svc</c> one the
    /// container would otherwise inject), so its <c>ILogger&lt;VoicePackRepository&gt;</c> is pulled
    /// from the container explicitly too.
    /// </summary>
    public static IServiceCollection AddVoicePackStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IVoicePackStore>(sp => new VoicePackRepository(
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
            sp.GetRequiredService<ILogger<VoicePackRepository>>()));
}
