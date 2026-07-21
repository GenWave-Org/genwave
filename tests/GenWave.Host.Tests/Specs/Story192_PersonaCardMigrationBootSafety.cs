// STORY-192 — Persona card migration boot safety (bug-fix regression)
//
// BDD specification — xUnit. Confirmed review finding (post-#54,
// PersonaCardMigrationServiceCollectionExtensions): the DI factory wired by
// AddGenWavePersonaCardMigration used to call `new NpgsqlDataSourceBuilder(stationConnStr).Build()`
// EAGERLY, inline in the factory delegate. An empty/dev-mode ConnectionStrings:Station — the shipped
// default, and a documented supported "no persona" deployment (see PersonaRepository's own remarks) —
// makes that call throw ArgumentException ("Host can't be null") the instant IHost.StartAsync resolves
// PersonaCardMigrationHostedService, well before PersonaCardMigrator.RunAsync's own try/catch (WARN +
// next-boot retry) ever gets a turn. This killed host boot outright, contradicting the extension's own
// XML doc and the three sibling seams (AddPersonaStore, AddPersonaMemoryStore, AddBoothLog) that all
// wrap the same Build() call in a Lazy<NpgsqlDataSource> for exactly this reason.
//
// Every WebApplicationFactory-based spec elsewhere in this project removes ALL IHostedServices (see
// Story120's PersonaApiWebFactory), so nothing exercised the real DI-composition boot path and this
// slipped through every existing suite. This file boots the real Program.cs graph with
// PersonaCardMigrationHostedService deliberately KEPT — every other hosted service removed purely for
// isolation (no real Liquidsoap/Kokoro/health-probe connections belong in this fact) — against an
// explicitly empty Station connection string, and asserts the host starts and degrades to a WARN
// instead of crashing.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Host.Seeding;
using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for assertion (mirrors
/// Story120's own copy of this idiom — file-scoped, so each spec file defines its own).
/// </summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}

// ── WebApplicationFactory for the boot-safety fact ───────────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph with <c>ConnectionStrings:Station</c> explicitly empty and
/// <see cref="PersonaCardMigrationHostedService"/> deliberately kept (every other hosted service
/// removed — this fact isolates the one seam under test). Also swaps in
/// <see cref="MigratorLogger"/> for <see cref="ILogger{T}"/> of <see cref="PersonaCardMigrator"/> so
/// the scenario can assert the degrade path actually ran, not merely that boot didn't throw.
/// </summary>
file sealed class PersonaCardMigrationBootWebFactory : WebApplicationFactory<Program>
{
    public CapturingLogger<PersonaCardMigrator> MigratorLogger { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (mirrors Story120's
        // PersonaApiWebFactory). ConnectionStrings:Station is set explicitly rather than relying on
        // appsettings' own empty default, so this fact keeps failing loudly if that default ever
        // changes.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Station", "");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically), so no process env var
        // is mutated and no other test class can race with this per-instance value. A
        // non-reachable host is fine — this fact never resolves IMediaCatalog.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            // Keep ONLY PersonaCardMigrationHostedService — every other hosted service would attempt
            // a real Liquidsoap/Postgres/dependency-health connection this fact has no interest in.
            services.RemoveAll<IHostedService>();
            services.AddHostedService<PersonaCardMigrationHostedService>();

            // Registered after logging's own open-generic ILogger<> binding, so this specific
            // closed-generic singleton wins on resolution for PersonaCardMigrator.
            services.AddSingleton<ILogger<PersonaCardMigrator>>(MigratorLogger);
        });
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaCardMigrationBootSafety
{
    public sealed class ScenarioEmptyStationConnectionStringDegradesInsteadOfCrashing
    {
        [Fact]
        public async Task TheHostStartsWithoutThrowing()
        {
            // The bug: this used to throw ArgumentException ("Host can't be null") synchronously out
            // of host startup — DI resolves PersonaCardMigrationHostedService (and therefore its
            // eagerly-built NpgsqlDataSource) the moment IHost.StartAsync runs, well before
            // PersonaCardMigrator.RunAsync's own try/catch is ever reached. Fixed, the empty
            // connection string surfaces no earlier than the first RunAsync tick, inside that catch.
            await using var factory = new PersonaCardMigrationBootWebFactory();

            using var client = factory.CreateClient();
        }

        [Fact]
        public async Task AWarnIsLoggedInsteadOfTheHostCrashing()
        {
            await using var factory = new PersonaCardMigrationBootWebFactory();
            using var client = factory.CreateClient();

            // PersonaCardMigrationHostedService.ExecuteAsync isn't awaited by StartAsync (fire-and-
            // forget, F71.2) — poll briefly for the WARN PersonaCardMigrator.RunAsync's catch logs.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (factory.MigratorLogger.Warnings.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            var warning = Assert.Single(factory.MigratorLogger.Warnings);
            Assert.Contains("Persona card migration failed", warning);
        }
    }
}
