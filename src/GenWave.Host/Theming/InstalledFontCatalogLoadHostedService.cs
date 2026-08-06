namespace GenWave.Host.Theming;

/// <summary>
/// Runs <see cref="InstalledFontCatalog.ReloadAsync"/> once per boot, without blocking host startup
/// (SPEC F104.6/F104.8, STORY-283, PLAN T200) — the exact <see cref="ThemeCatalogOwnerLoadHostedService"/>
/// precedent, at the font altitude. <see cref="InstalledFontCatalog.ReloadAsync"/> itself never lets
/// an exception escape (an unreachable/empty <c>station.font_pack</c>(+<c>_face</c>) store degrades to
/// the empty, vendored-only snapshot the catalog was constructed with, WARN-logged — SPEC F104.8's
/// offline floor), so the catch here is a last-resort guard, not a path this service relies on for
/// normal degrade-and-retry behaviour.
///
/// Constructor-injecting the shared <see cref="InstalledFontCatalog"/> singleton is what forces its
/// construction at host start (<see cref="InstalledFontCatalog.Create"/> reads nothing merely by
/// being constructed, so this never risks a DB call before <see cref="ExecuteAsync"/> itself runs) —
/// every other consumer (the widened <c>GET /fonts/{file}</c> route) receives the SAME instance, so
/// once this service's one <see cref="InstalledFontCatalog.ReloadAsync"/> call completes, every
/// subsequent request sees the vendored ∪ installed set with no restart in between.
/// </summary>
sealed class InstalledFontCatalogLoadHostedService(
    InstalledFontCatalog catalog,
    ILogger<InstalledFontCatalogLoadHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await catalog.ReloadAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the load finished; the next boot retries.
        }
        catch (Exception ex)
        {
            // Should be unreachable — ReloadAsync itself never throws for an expected failure. Kept
            // as a last-resort guard so a bug in the installed-font-load pipeline can never kill an
            // otherwise-healthy host.
            logger.LogWarning(ex,
                "Installed font catalog load: unexpected failure outside InstalledFontCatalog's own degrade path — host starting normally");
        }
    }
}
