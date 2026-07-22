using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    /// <summary>
    /// Registers <see cref="IPersonaMemory"/> (SPEC F71.4-F71.6, STORY-194) the same lazy way
    /// <see cref="AddPersonaStore"/> registers <see cref="IPersonaStore"/>, over the same
    /// <paramref name="connectionString"/> — and binds/validates <see cref="PersonaMemoryOptions"/>
    /// (<c>Persona:Memory:CapPerKind</c>) from <paramref name="configuration"/>.
    ///
    /// T38 ships this registration deliberately without a Host call site (mirrors
    /// <see cref="IPersonaStore"/>'s own original shape — "no DI registration and no consumer land
    /// with this seam" — except here the DI half is what this task delivers; the plan sequences
    /// wiring the actual Host call, alongside the prompt-assembly consumer, with Q4's persona work).
    /// </summary>
    public static IServiceCollection AddPersonaMemoryStore(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services
            .AddOptions<PersonaMemoryOptions>()
            .Bind(configuration.GetSection(PersonaMemoryOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddSingleton<IPersonaMemory>(sp =>
            new PersonaMemoryRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
                sp.GetRequiredService<IOptions<PersonaMemoryOptions>>()));
    }

    /// <summary>
    /// Registers <see cref="IPersonaTasteStore"/> (SPEC F82.1, F84.1-F84.3; STORY-213) the same lazy
    /// way <see cref="AddPersonaStore"/> registers <see cref="IPersonaStore"/>, over the same
    /// <paramref name="connectionString"/>. No options to bind — F84.3's cap/eviction tunables belong
    /// to the accrual write path (T70), not this seam.
    ///
    /// T59 ships this registration deliberately without a Host call site (mirrors
    /// <see cref="AddPersonaMemoryStore"/>'s own original shape): the ranker (T63) and card import
    /// (T66-T69) are the first consumers, landing in later tasks.
    /// </summary>
    public static IServiceCollection AddPersonaTasteStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaTasteStore>(
            _ => new PersonaTasteRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));

    /// <summary>
    /// Registers <see cref="IPersonaImportStore"/> (SPEC F79.3, F79.6; STORY-209, PLAN T67) the same
    /// lazy way <see cref="AddPersonaStore"/> registers <see cref="IPersonaStore"/>, over the same
    /// <paramref name="connectionString"/> — a SEPARATE <see cref="NpgsqlDataSource"/> instance from
    /// the other three (rather than sharing one), matching this codebase's existing "each store
    /// builds its own lazy data source" convention throughout this file; Npgsql pools connections per
    /// data source, so this costs one extra idle pool, not one extra live connection.
    /// </summary>
    public static IServiceCollection AddPersonaImportStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaImportStore>(
            _ => new PersonaImportRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));

    /// <summary>
    /// Registers <see cref="IPersonaTasteAccrualStore"/> (SPEC F84.1-F84.6; STORY-215, PLAN T70) the
    /// same lazy way <see cref="AddPersonaStore"/> registers <see cref="IPersonaStore"/>, over the
    /// same <paramref name="connectionString"/> — a SEPARATE <see cref="NpgsqlDataSource"/> instance
    /// from <see cref="AddPersonaTasteStore"/>'s (mirrors <see cref="AddPersonaImportStore"/>'s own
    /// remarks: one extra idle pool, not one extra live connection). This store reads/writes both
    /// <c>station.booth_log</c> (attribution) and <c>station.persona_taste</c> (the nudge/eviction) in
    /// one transaction, so it is deliberately its own repository rather than a method added to
    /// <see cref="PersonaTasteRepository"/> or <c>BoothLogRepository</c>.
    /// </summary>
    public static IServiceCollection AddPersonaTasteAccrualStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaTasteAccrualStore>(
            _ => new PersonaTasteAccrualRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
