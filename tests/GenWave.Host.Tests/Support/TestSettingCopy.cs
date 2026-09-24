using GenWave.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Builds a real <see cref="SettingCopy"/> over the genuine shipped
/// <c>Configuration/SettingsResources.resx</c> (STORY-477) —
/// the identical <c>AddLocalization()</c> registration <c>Program.cs</c> wires for production,
/// resolved through a throwaway <see cref="ServiceProvider"/> rather than a no-DI fallback baked
/// into <see cref="GenWave.Host.Api.SettingsController"/> itself. <see cref="SettingCopy"/> is now a
/// REQUIRED <c>SettingsController</c> constructor parameter (matching <c>iconPackStore</c>'s own
/// posture — see that controller's own remarks), so every <c>new SettingsController(...)</c> unit
/// test passes <see cref="Real"/>() — its label/help/group/choice copy comes from the real resx, matching what a production
/// request actually serves.
/// </summary>
internal static class TestSettingCopy
{
    public static SettingCopy Real()
    {
        var services = new ServiceCollection();
        // ResourceManagerStringLocalizerFactory itself takes an ILoggerFactory dependency —
        // AddLocalization() alone doesn't register one, so this throwaway container needs its own
        // (AddLogging() with no providers wired is enough; nothing here ever logs).
        services.AddLogging();
        services.AddLocalization();
        using var provider = services.BuildServiceProvider();
        return new SettingCopy(provider.GetRequiredService<IStringLocalizer<SettingsResources>>());
    }
}
