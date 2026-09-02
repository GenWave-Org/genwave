namespace GenWave.Ads;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Reads <c>Station:Ads:TargetCount</c>/<c>RefreshDays</c>/<c>AutoApprove</c> straight off the live
/// <see cref="IConfiguration"/> tree (SPEC F159.3, F159.4; STORY-389; PLAN T402) — the deliberate split
/// <c>StationAdsOptions</c>' own PLAN T397 remarks already call out: <c>Station:Ads:EveryNUnits</c>
/// binds through <c>StationAdsOptions</c> (GenWave.Host) and <c>AntiRepeatWindow</c> through
/// <see cref="AdSpotAntiRepeatOptions"/> (this project), but these three have "no bound options class
/// anywhere yet" by design — the natural strongly-typed home for them is EITHER a Host-side options
/// class <c>GenWave.Ads</c> could never reference (L10) OR a THIRD Live-shaped options class in this
/// project duplicating <see cref="AdSpotAntiRepeatOptions"/>'s own shape for no benefit, since
/// <c>AdSpotWorker</c> is their only reader and already needs live re-evaluation every tick regardless
/// of which mechanism supplies it. A raw read costs nothing extra here and needs no new DI
/// registration.
///
/// <para>
/// <b>Genuinely live.</b> <see cref="IConfiguration"/> itself is backed by
/// <c>StationSettingsConfigurationProvider</c> (a live, Postgres-reloading provider — the SAME reload
/// token every <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> in this codebase
/// ultimately subscribes to), so a fresh read on every tick reflects a <c>PUT /api/settings</c> edit
/// with no api restart, exactly like every other Live knob this codebase documents.
/// </para>
///
/// <para>
/// <b>Fail-safe, not fail-closed (T402's own choice, matching this codebase's pervasive posture for a
/// malformed live value — e.g. <c>AdRenderService.ParseVoicePlan</c>'s identical "degrade to the safe
/// default" shape one file over).</b> <see cref="SettingValidator"/> (GenWave.Host) already range-checks
/// every PUT before it ever reaches this table, so a malformed value here would mean a data-integrity
/// bug elsewhere, not an expected input — but <see cref="ConfigurationBinder.GetValue{T}(IConfiguration,string,T)"/>
/// still throws on an unconvertible (not merely missing) value, and a worker tick must never die over
/// a stray operator typo in raw config. Each read is individually guarded, falling back to the SAME
/// default SPEC F163.1 documents.
/// </para>
/// </summary>
public static class AdStockSettingsReader
{
    internal const int DefaultTargetCount = 12;
    internal const int DefaultRefreshDays = 30;
    internal const bool DefaultAutoApprove = false;

    public static AdStockSettings Read(IConfiguration configuration) => new(
        TargetCount: ReadOrDefault(configuration, "Station:Ads:TargetCount", DefaultTargetCount),
        RefreshDays: ReadOrDefault(configuration, "Station:Ads:RefreshDays", DefaultRefreshDays),
        AutoApprove: ReadOrDefault(configuration, "Station:Ads:AutoApprove", DefaultAutoApprove));

    static T ReadOrDefault<T>(IConfiguration configuration, string key, T fallback) where T : struct
    {
        try
        {
            return configuration.GetValue(key, fallback);
        }
        catch (InvalidOperationException)
        {
            // GetValue<T> throws only when the key IS present but fails to convert — a data-integrity
            // bug (SettingValidator already range-checked this at write time), not an expected input.
            return fallback;
        }
    }
}
