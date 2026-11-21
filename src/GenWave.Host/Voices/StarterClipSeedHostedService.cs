namespace GenWave.Host.Voices;

/// <summary>Runs <see cref="StarterClipSeeder"/> once at boot without delaying startup (SPEC F219.14). The seeder never throws for a bad clip or an unreachable database.</summary>
sealed class StarterClipSeedHostedService(StarterClipSeeder seeder) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => seeder.SeedAsync(stoppingToken);
}
