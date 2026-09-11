namespace GenWave.Ads;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Reads <c>Station:Ads:AnnouncerVoice</c>/<c>CastVoices</c> off the live <see cref="IConfiguration"/>
/// tree (SPEC F167.1, F167.2; STORY-402; PLAN T415) — the <see cref="AdStockSettingsReader"/> shape
/// applied to the cast-pool pair beside it: that class's own remarks on why these
/// <c>Station:Ads:*</c> knobs stay raw <see cref="IConfiguration"/> reads rather than a bound options
/// class apply identically here (T417 registered these two on the SAME allowlist, in the SAME
/// namespace, on purpose — see <c>GenWave.Host.Configuration.StationSettingsAllowlist</c>'s own
/// remarks, plain text not a <c>cref</c>, L10).
///
/// <para>
/// <b>Defensive about whitespace/empties only — never format.</b>
/// <c>GenWave.Host.Configuration.SettingValidator</c> already range/format-checks every PUT before it
/// ever reaches this table; this reader's own job is to turn whatever raw string sits there into a
/// clean candidate list — trimming each entry, dropping blanks a stray leading/trailing/double comma
/// would leave behind, and de-duplicating while preserving the operator's own ordering
/// (<see cref="AdCastPicker.Pick"/>'s own indexed pick needs a stable, order-preserving list to stay
/// deterministic across reads of the SAME configured value).
/// </para>
/// </summary>
/// <remarks>Public, not internal (PLAN T442 ruling) — carried along with <see cref="AdLiveSettings"/>'s
/// own visibility change (see that record's remarks); nothing else about this reader's contract
/// changes.</remarks>
public static class AdLiveSettingsReader
{
    internal const string DefaultAnnouncerVoice = "";

    /// <summary>SPEC F168.4's own default — matches <c>appsettings.json</c>'s <c>Station:Ads:BedFadeMs</c>
    /// seed and <c>GenWave.Host.Configuration.SettingValidator</c>'s own range (T417) exactly (plain
    /// text, not a <c>cref</c> — GenWave.Ads never references GenWave.Host, L10).</summary>
    internal const int DefaultBedFadeMs = 300;

    /// <summary>
    /// The SAME 100-1000 range <c>SettingValidator.AdsBedFadeMsMin</c>/<c>AdsBedFadeMsMax</c>
    /// already enforces at PUT-time (GenWave.Host, T417) — hardcoded here rather than referenced
    /// (L10: GenWave.Ads must never reference GenWave.Host) as a defensive second gate: a value that
    /// somehow reached this table unvalidated (a direct DB edit, a future write path that skips the
    /// validator) still clamps to a sane render before it ever reaches ffmpeg — the same
    /// "belt-and-suspenders, not redundant" posture the ad-spot store's own never-overwrite
    /// <c>coalesce</c> guards apply one seam over (plain text, not a <c>cref</c>: GenWave.Ads does not
    /// reference GenWave.MediaLibrary.Station).
    /// </summary>
    internal const int MinBedFadeMs = 100;
    internal const int MaxBedFadeMs = 1000;

    /// <summary>gh-#746 — <c>Station:Ads:BedDuckDb</c>'s own default and clamp: how many dB UNDER the
    /// voice the background music sits in a generated ad (the mixer makes it relative to what the voice
    /// and bed actually measure). −12 is the ordinary radio bed level; 0 = no ducking at all; −60 =
    /// effectively silent. Mirrors <c>appsettings.json</c>'s <c>Station:Ads:BedDuckDb</c> and
    /// <c>SettingValidator</c>'s <c>AdsBedDuckDbMin/Max</c> — change one, change all three.</summary>
    internal const double DefaultBedDuckDb = -12.0;
    internal const double MinBedDuckDb = -60.0;
    internal const double MaxBedDuckDb = 0.0;

    public static AdLiveSettings Read(IConfiguration configuration) => new(
        AnnouncerVoice: ReadString(configuration, "Station:Ads:AnnouncerVoice", DefaultAnnouncerVoice).Trim(),
        CastVoices: ParseCastVoices(ReadString(configuration, "Station:Ads:CastVoices", "")),
        BedFadeMs: Math.Clamp(
            AdSettingsRead.OrDefault(configuration, "Station:Ads:BedFadeMs", DefaultBedFadeMs), MinBedFadeMs,
            MaxBedFadeMs),
        BedDuckDb: Math.Clamp(
            AdSettingsRead.OrDefault(configuration, "Station:Ads:BedDuckDb", DefaultBedDuckDb), MinBedDuckDb,
            MaxBedDuckDb));

    static string ReadString(IConfiguration configuration, string key, string fallback)
    {
        try
        {
            return configuration.GetValue(key, fallback) ?? fallback;
        }
        catch (InvalidOperationException)
        {
            // Mirrors AdSettingsRead.OrDefault's own guard (this class's own struct-typed reads go
            // through that shared helper directly; string is not a struct, so this copy stays): GetValue<T>
            // throws only when the key IS present but fails to convert — never expected for a
            // string-typed value, kept for the SAME "one stray operator typo must never crash a worker
            // tick" reason.
            return fallback;
        }
    }

    static IReadOnlyList<string> ParseCastVoices(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
