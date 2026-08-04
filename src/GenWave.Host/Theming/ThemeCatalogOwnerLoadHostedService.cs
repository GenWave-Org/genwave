namespace GenWave.Host.Theming;

/// <summary>
/// Runs <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> once per boot, without blocking host
/// startup (SPEC F103.7, STORY-271, PLAN T182) — mirrors
/// <c>PersonaCardMigrationHostedService</c>/<c>SafeLoopSeedHostedService</c>'s own fire-and-forget
/// shape. <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> itself never lets an exception escape (an
/// unreachable/empty <c>station.theme</c> store degrades to the shipped-only set, WARN-logged — SPEC
/// F102.7's offline floor), so the catch here is a last-resort guard, not a path this service relies
/// on for normal degrade-and-retry behaviour.
///
/// Constructor-injecting the shared <see cref="ThemeCatalog"/> singleton is what forces its
/// construction at host start (<see cref="ThemeCatalog.CreateForStation"/> reads only embedded
/// resources, so this never risks a DB call before <see cref="ExecuteAsync"/> itself runs) — every
/// other consumer (the theme endpoints) receives the SAME instance, so once this service's one
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> call completes, every subsequent request sees
/// the shipped ∪ owner set with no restart in between.
/// </summary>
sealed class ThemeCatalogOwnerLoadHostedService(
    ThemeCatalog catalog,
    ILogger<ThemeCatalogOwnerLoadHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await catalog.ReloadOwnerThemesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the load finished; the next boot retries.
        }
        catch (Exception ex)
        {
            // Should be unreachable — ReloadOwnerThemesAsync itself never throws for an expected
            // failure. Kept as a last-resort guard so a bug in the owner-load pipeline can never kill
            // an otherwise-healthy host.
            logger.LogWarning(ex,
                "Owner theme load: unexpected failure outside ThemeCatalog's own degrade path — host starting normally");
        }
    }
}
