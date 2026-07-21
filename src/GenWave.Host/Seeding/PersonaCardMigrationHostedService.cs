using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Seeding;

/// <summary>
/// Runs <see cref="PersonaCardMigrator.RunAsync"/> once per boot, without blocking host startup
/// (SPEC F71.2, STORY-192) — mirrors <see cref="SafeLoopSeedHostedService"/>'s fire-and-forget shape.
/// <see cref="PersonaCardMigrator.RunAsync"/> itself never lets an exception escape (a WARN and a
/// next-boot retry come back instead), so the catch here is a last-resort guard, not a path this
/// service relies on for normal degrade-and-retry behaviour.
/// </summary>
sealed class PersonaCardMigrationHostedService(
    PersonaCardMigrator migrator,
    ILogger<PersonaCardMigrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await migrator.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the migration finished; the next boot retries.
        }
        catch (Exception ex)
        {
            // Should be unreachable — RunAsync itself never throws for an expected failure. Kept as
            // a last-resort guard so a bug in the migration pipeline can never kill an otherwise-
            // healthy host.
            logger.LogWarning(ex,
                "Persona card migration: unexpected failure outside the migration pipeline — host starting normally");
        }
    }
}
