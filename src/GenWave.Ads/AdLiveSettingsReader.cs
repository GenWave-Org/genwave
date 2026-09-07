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
internal static class AdLiveSettingsReader
{
    internal const string DefaultAnnouncerVoice = "";

    public static AdLiveSettings Read(IConfiguration configuration) => new(
        AnnouncerVoice: ReadString(configuration, "Station:Ads:AnnouncerVoice", DefaultAnnouncerVoice).Trim(),
        CastVoices: ParseCastVoices(ReadString(configuration, "Station:Ads:CastVoices", "")));

    static string ReadString(IConfiguration configuration, string key, string fallback)
    {
        try
        {
            return configuration.GetValue(key, fallback) ?? fallback;
        }
        catch (InvalidOperationException)
        {
            // Mirrors AdStockSettingsReader.ReadOrDefault: GetValue<T> throws only when the key IS
            // present but fails to convert — never expected for a string-typed value, kept for the
            // SAME "one stray operator typo must never crash a worker tick" reason.
            return fallback;
        }
    }

    static IReadOnlyList<string> ParseCastVoices(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
