using System.ComponentModel.DataAnnotations;

namespace GenWave.Tts;

/// <summary>
/// Configuration for two-voice banter generation (SPEC F127.4, F127.8, STORY-326). The
/// duration-fit knob landed at PLAN T282; <see cref="Shows"/>/<see cref="EveryNthAiring"/> (SPEC
/// F127.8's scope/cadence pair) join it at PLAN T285 — read through
/// <c>GenWave.Core.Abstractions.ICrosstalkScopeProvider</c>
/// (<c>GenWave.Host.Options.OptionsMonitorCrosstalkScopeProvider</c>'s own binding) by
/// <c>GenWave.Orchestration.CrosstalkPlanner</c>, never bound directly by this project (which has
/// no reference to <c>GenWave.Orchestration</c>, an L1 project one layer further out).
/// </summary>
public sealed class CrosstalkOptions
{
    public const string Section = "Crosstalk";

    /// <summary>
    /// The spoken-duration target a validated <see cref="GenWave.Core.Domain.CrosstalkAiredScript"/> must fit under (SPEC
    /// F127.4) — an estimate over this rejects the WHOLE exchange (never a trim; see
    /// <see cref="CrosstalkScriptParser"/>'s own remarks). Defaults to the spec'd 25 seconds. Live via
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>, read fresh by
    /// <see cref="CrosstalkScriptWriter"/> on every generation attempt (mirrors every other
    /// live-adjustable leaf this project's options classes carry), so an operator PUT reaches the
    /// very next attempt with no api restart.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DurationTargetSeconds { get; set; } = 25;

    /// <summary>
    /// Raw JSON array of enabled show SLUGS (<c>station.show.slug</c>, db/35's own unique stable
    /// identity), e.g. <c>["morning-drive"]</c> — the same opaque-string-kind idiom
    /// <c>Station:Envelope:Genres</c> uses (see
    /// <c>GenWave.Host.Options.OptionsMonitorStationDefaultEnvelopeSource.ParseGenres</c>'s own
    /// remarks): the station-settings overlay only expands a stored JSON array into indexed
    /// <c>IConfiguration</c> keys for arrays of scalars it already knows how to bind as a typed
    /// list; a raw string here keeps this on the shape every other free-text leaf already uses.
    /// Deliberately SLUGS, not display names (PLAN T285 review F4) — the T175 "names slugs, not
    /// labels" rule <c>SettingValidator</c>'s own <c>Station:Theme</c> guard already follows: a show's
    /// mutable, non-unique display <c>Name</c> would let an operator's rename silently kill banter
    /// forever. Null, blank, or malformed JSON all mean NO enabled shows — SPEC F127.8's fail-closed
    /// "empty means the feature is off" ruling: no station's sound changes on upgrade until an
    /// operator explicitly names a show. Matched case-insensitively against
    /// <c>GenWave.Core.Domain.ShowSummary.Slug</c>. Parsing this is
    /// <c>GenWave.Host.Options.OptionsMonitorCrosstalkScopeProvider</c>'s job, not this class's.
    /// </summary>
    public string? Shows { get; set; }

    /// <summary>
    /// How many eligible airings of an enabled show pass before one carries banter (SPEC F127.8,
    /// "1 every X shows"). Defaults to 1 — every eligible airing carries banter until an operator
    /// widens the cadence. The counting itself is a LATER task's own concern (PLAN T287's vend
    /// gate) — this is only the live knob.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int EveryNthAiring { get; set; } = 1;
}
