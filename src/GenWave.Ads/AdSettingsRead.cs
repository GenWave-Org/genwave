namespace GenWave.Ads;

using Microsoft.Extensions.Configuration;

/// <summary>
/// PLAN T416 review F4 — the one shared "read a live config value, fall back on an unconvertible-but-
/// present one" helper <see cref="AdStockSettingsReader"/> and <see cref="AdLiveSettingsReader"/> each
/// kept a byte-identical private copy of before this extraction. Both readers' own remarks explain
/// WHY the guard exists (<see cref="ConfigurationBinder.GetValue{T}(IConfiguration,string,T)"/> throws
/// only when the key IS present but fails to convert — a data-integrity bug
/// <c>GenWave.Host.Configuration.SettingValidator</c> should already have caught at write time, never
/// an expected input — and a worker tick must never die over a stray operator typo in raw config); this
/// type owns that ONE explanation instead of two hand-synced ones.
/// </summary>
static class AdSettingsRead
{
    internal static T OrDefault<T>(IConfiguration configuration, string key, T fallback) where T : struct
    {
        try
        {
            return configuration.GetValue(key, fallback);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }
}
