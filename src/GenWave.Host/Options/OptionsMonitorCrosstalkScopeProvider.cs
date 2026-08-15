namespace GenWave.Host.Options;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Tts;

/// <summary>
/// The Host-side half of the <see cref="ICrosstalkScopeProvider"/> seam (SPEC F127.8, STORY-328,
/// PLAN T285): wraps <see cref="IOptionsMonitor{TOptions}"/> so
/// <c>GenWave.Orchestration.CrosstalkPlanner</c> reads the SAME live values <c>PUT /api/settings</c>
/// writes — mirrors <see cref="OptionsMonitorShowPatterCadenceProvider"/> one seam over.
///
/// <para>
/// <see cref="CrosstalkOptions.Shows"/> is a raw JSON array of show SLUGS, not display names (PLAN
/// T285 review F4 — the <c>Station:Envelope:Genres</c> opaque-string-kind idiom — see
/// <see cref="OptionsMonitorStationDefaultEnvelopeSource.ParseGenres"/>'s own remarks for why a raw
/// string, not a bound <c>IList&lt;string&gt;</c>): null, blank, or malformed JSON all degrade to
/// an EMPTY enabled-show list — SPEC F127.8's fail-closed "empty means the feature is off" ruling,
/// so an operator's JSON typo can only ever turn crosstalk off, never on for a show never named.
/// </para>
///
/// <para>
/// <see cref="ParseShows"/> also drops any null/blank entry (PLAN T285 review F9) — the shape
/// <c>GenWave.Host.Configuration.SettingValidator</c>'s own <c>Crosstalk:Shows</c> guard rejects at
/// write time, but an env var or a hand-edited <c>appsettings.json</c> reaches
/// <see cref="IOptionsMonitor{TOptions}"/> WITHOUT ever passing through that validator — a stray
/// empty/null element there must not silently become a show every unnamed/music-only block matches.
/// </para>
/// </summary>
sealed class OptionsMonitorCrosstalkScopeProvider(
    IOptionsMonitor<CrosstalkOptions> crosstalkMonitor, ILogger<OptionsMonitorCrosstalkScopeProvider> logger)
    : ICrosstalkScopeProvider
{
    public IReadOnlyList<string> EnabledShows => ParseShows(crosstalkMonitor.CurrentValue.Shows, logger);

    public int EveryNthAiring => crosstalkMonitor.CurrentValue.EveryNthAiring;

    static IReadOnlyList<string> ParseShows(string? raw, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        List<string?> parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<string?>>(raw) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Crosstalk:Shows could not be parsed; treating as no enabled shows (the feature stays off) until fixed");
            return [];
        }

        // PLAN T285 review F9 — a null/blank entry can only arrive via a path SettingValidator never
        // guarded (env var, hand-edited appsettings.json); drop it rather than let it match anything.
        var slugs = new List<string>();
        foreach (var entry in parsed)
        {
            if (!string.IsNullOrWhiteSpace(entry))
                slugs.Add(entry);
        }

        return slugs;
    }
}
