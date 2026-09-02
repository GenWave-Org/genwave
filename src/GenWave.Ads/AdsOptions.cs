using System.ComponentModel.DataAnnotations;

namespace GenWave.Ads;

/// <summary>
/// SPEC F163.2 (STORY-388, PLAN T396): env/compose-only knobs for the ads seam, config section
/// <c>Ads</c> (<c>Ads__*</c> on Docker Compose), bound top-level in <see cref="AdsServiceCollectionExtensions.AddGenWaveAds"/>
/// so <c>ValidateDataAnnotations()</c> genuinely enforces every <see cref="RangeAttribute"/> below at
/// boot — the same "top-level binds, nested don't" shape
/// <c>GenWave.MediaLibrary.Options.GardenerOptions</c>/<c>GenWave.Host.Options.AnnouncementsOptions</c>
/// already document (both live outside this project's reference graph — plain code text, not a
/// <c>cref</c>, on purpose).
///
/// <para>
/// <b>The GardenerOptions lesson (F163.2's own citation):</b> <see cref="RangeAttribute(double, double)"/>
/// deliberately, NOT the <c>(int, int)</c> overload, for <see cref="DurationToleranceRatio"/> and
/// <see cref="BedDuckDb"/> — on an <c>int</c>-typed <see cref="RangeAttribute"/>, an out-of-range
/// <c>double</c> config value is converted via <c>Convert.ToInt32</c> (banker's rounding,
/// <see cref="MidpointRounding.ToEven"/>) BEFORE the comparison, so a genuinely out-of-range value
/// can boot clean with its real, un-rounded value silently outside the documented range
/// (<c>GardenerOptions.NudgeGain</c>'s own remarks carry the full net10-verified example).
/// </para>
///
/// <para>
/// <b>Which knob T396 actually reads:</b> only <see cref="LibraryName"/> —
/// <see cref="AdsLibrarySeeder"/> (create-if-absent) and <see cref="LibraryAdSpotSource"/>
/// (library-id resolution) both key off it. <see cref="DurationToleranceRatio"/> is T399's
/// (<c>AdScriptValidator</c>'s duration-fit check, SPEC F160.3); <see cref="WorkerIntervalMinutes"/>
/// and <see cref="BedDuckDb"/> are T401's (<c>AdSpotWorker</c>'s tick cadence and offline bed-duck
/// mix, SPEC F161.1/F161.2). All four bind and boot-validate now regardless — one options class born
/// once at T396, never re-touched just because a later task starts reading a field that was already
/// here (F163.2 lists all four together for exactly this reason).
/// </para>
/// </summary>
public sealed class AdsOptions
{
    public const string SectionName = "Ads";

    /// <summary>
    /// Allowed fractional deviation between a script's estimated read time and its target
    /// <c>spot_seconds</c> before <c>AdScriptValidator</c> (T399) refuses it as over (SPEC F160.3).
    /// Default 0.4 (SPEC F163.2's own explicit default). Range 0.0-2.0 (T396's own choice, the
    /// <c>GardenerOptions.NudgeGain</c> shape for an unstated-in-SPEC bound): 0 requires an
    /// exact read-time match — no script would ever fit — 2.0 already triples the target length,
    /// past any sane spot.
    /// </summary>
    [Range(0.0, 2.0)]
    public double DurationToleranceRatio { get; set; } = 0.4;

    /// <summary>
    /// How often <c>AdSpotWorker</c>'s (T401) render tick runs, in minutes (SPEC F161.1). Default 10
    /// (SPEC F163.2's own explicit default). Range 1-1440 (T396's own choice, the
    /// <c>GardenerOptions.IntervalMinutes</c> shape): a day is the loosest cadence that still
    /// calls it a worker rather than a cron job nobody remembers exists.
    /// </summary>
    [Range(1, 1440)]
    public int WorkerIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Bed attenuation, in dB, relative to the voice in the offline ad mix (SPEC F161.2, T401).
    /// Default -12.0 (T396's own choice — F163.2 leaves this default unstated — matching
    /// <c>Station:Safe:BedDuckDb</c>'s identical concept). Range -60.0-0.0: 0 is no ducking at all,
    /// -60 is effectively silent.
    /// </summary>
    [Range(-60.0, 0.0)]
    public double BedDuckDb { get; set; } = -12.0;

    /// <summary>
    /// Display name of the seeded ads library (SPEC F158.5, F159.1) — <see cref="AdsLibrarySeeder"/>
    /// creates it if absent at boot; <see cref="LibraryAdSpotSource"/> resolves it back to a library
    /// id on every vend. Default "ads" (SPEC F163.2's own explicit default).
    /// </summary>
    public string LibraryName { get; set; } = "ads";

    /// <summary>
    /// The worst-case wall-clock budget <c>AdSpotWorker</c> (PLAN T402) gives ONE render attempt,
    /// via a linked <see cref="System.Threading.CancellationTokenSource.CancelAfter(TimeSpan)"/> on
    /// that tick's own claim — a wedged Kokoro (or a stalled ffmpeg mix) must not hang the worker's
    /// tick loop forever. Deliberately NOT <c>Tts:RenderBudgetSeconds</c> (the crosstalk stock
    /// worker's own knob, default 30s): that budget governs a DIFFERENT concern — the ON-AIR
    /// <c>PlayoutFeeder.RefillAsync</c> substitution window (gh-#184) — and an ad spot's own render
    /// genuinely needs more headroom (up to 3 voices, up to a 60s script, plus an optional bed mix)
    /// than a single on-air line ever does. Default 180 (three minutes — comfortably above the
    /// slowest realistic 3-voice/60s spot on modest hardware, per T402's own build-time sizing note;
    /// see <c>AdSpotLifecycleGuardianService</c>'s own remarks for how the guardian's re-arm grace is
    /// PINNED to always exceed this value, not merely tuned near it). Range 10-1800 (T402's own
    /// choice, the <c>GardenerOptions.IntervalMinutes</c> shape): under 10s could not realistically
    /// complete even a 15s single-voice spot; half an hour is already far past any sane render.
    /// </summary>
    [Range(10, 1800)]
    public int RenderBudgetSeconds { get; set; } = 180;
}
