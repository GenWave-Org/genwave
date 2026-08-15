using System.ComponentModel.DataAnnotations;

namespace GenWave.Tts;

/// <summary>
/// Configuration for two-voice banter generation (SPEC F127.4, F127.8, STORY-326). Only the
/// duration-fit knob lands here at PLAN T282 — <c>Crosstalk:Shows</c>/<c>Crosstalk:EveryNthAiring</c>
/// (SPEC F127.8's scope/cadence pair) are a LATER task's own concern (T284's <c>CrosstalkPlanner</c>),
/// not read by anything this project builds.
/// </summary>
public sealed class CrosstalkOptions
{
    public const string Section = "Crosstalk";

    /// <summary>
    /// The spoken-duration target a validated <see cref="CrosstalkScript"/> must fit under (SPEC
    /// F127.4) — an estimate over this rejects the WHOLE exchange (never a trim; see
    /// <see cref="CrosstalkScriptParser"/>'s own remarks). Defaults to the spec'd 25 seconds. Live via
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>, read fresh by
    /// <see cref="CrosstalkScriptWriter"/> on every generation attempt (mirrors every other
    /// live-adjustable leaf this project's options classes carry), so an operator PUT reaches the
    /// very next attempt with no api restart.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DurationTargetSeconds { get; set; } = 25;
}
