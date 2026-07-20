using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IPersonaStore"/> (SPEC F35.1, STORY-118/120) — first consumer is
/// <c>PersonaController</c> (Host), STORY-120. Deliberately separate from
/// <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: personas live in the
/// <c>station</c> schema/role (<c>station_svc</c>, the same connection the F19 settings overlay
/// uses), not <c>library</c> — a distinct <see cref="NpgsqlDataSource"/> on its own connection
/// string rather than folded into the library's.
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPersonaStore"/> over a dedicated <see cref="NpgsqlDataSource"/> built
    /// from <paramref name="connectionString"/> (the same value the caller passes to
    /// <c>ConnectionStrings:Station</c>-backed services). The data source build itself is wrapped
    /// in a <see cref="Lazy{T}"/> (T37, STORY-193 review finding) rather than run inline in this
    /// factory: an empty connection string (dev/test hosts, or any deployment that simply doesn't
    /// use personas — <c>ConnectionStrings:Station</c> defaults to <c>""</c>) throws the moment
    /// <see cref="NpgsqlDataSourceBuilder.Build"/> runs, and merely RESOLVING
    /// <see cref="IPersonaStore"/> from DI — which now happens on every TTS render via
    /// <c>ActivePersonaCorrectionsCache</c>, not only on persona-specific requests — must never be
    /// enough to trigger that. See <see cref="PersonaRepository"/>'s own remarks for the full
    /// "resolves AND USES" distinction this preserves.
    /// </summary>
    public static IServiceCollection AddPersonaStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaStore>(
            _ => new PersonaRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
