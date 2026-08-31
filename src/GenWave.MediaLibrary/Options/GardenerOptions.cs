using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Options;

/// <summary>
/// The Library Gardener's boot-validated knobs (SPEC F155.1, STORY-380, PLAN T357, gh-#529):
/// config section <c>Gardener</c>, every key <c>Gardener__*</c> env/compose-only — the SAME
/// "top-level binds, nested don't" shape <see cref="GenWave.Host.Options.AnnouncementsOptions"/>'s
/// own remarks document, so <c>.ValidateDataAnnotations()</c> genuinely enforces every
/// <see cref="RangeAttribute"/> below at boot. The one Live exception —
/// <c>Station:Thumbs:Enabled</c> — is NOT on this class; it lives on the settings allowlist
/// (<see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>) per F155.1's own split.
/// Bound once, in <c>GenWave.MediaLibrary</c> (MediaLibraryServiceCollectionExtensions), so both
/// the gardener passes/thumb writes living in MediaLibrary and the Host's thumbs route limiter
/// read the SAME <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> instance rather
/// than two independently-bound copies.
///
/// <para>
/// F155.1 states an explicit default AND range for <see cref="NudgeGain"/>, <see cref="HalfLifeDays"/>,
/// and <see cref="Saturation"/> only; every other property's range below is this task's own
/// (T357) choice, documented on that property.
/// </para>
/// </summary>
public sealed class GardenerOptions
{
    public const string SectionName = "Gardener";

    /// <summary>Multiplies the rotation nudge into <c>PersonaRanker</c>'s rung-0 additive score
    /// term (SPEC F150.9, F152's own reuse map: <c>Score += Nudge × NudgeGain</c>). Default 0.5,
    /// range 0–2 — SPEC F155.1's own explicit bound: 0 disables the rotation signal outright
    /// without needing a second on/off knob, 2 is triple the weight of an already-maxed nudge.
    /// <see cref="RangeAttribute(double, double)"/> deliberately, NOT the <c>(int, int)</c>
    /// overload (T357 review HIGH-1): on an <c>int</c>-typed <see cref="RangeAttribute"/>, an
    /// out-of-range double config value is converted via <c>Convert.ToInt32</c> (banker's
    /// rounding, <see cref="MidpointRounding.ToEven"/>) BEFORE the comparison — verified on
    /// net10: <c>-0.5</c> rounds to <c>0</c> and <c>2.4</c>/<c>2.5</c> both round to <c>2</c>, so
    /// all three would have booted clean while leaving this property's REAL, un-rounded value
    /// out of the documented range (a negative value would invert the rotation signal entirely).
    /// The <c>(double, double)</c> overload compares the double directly, no conversion step.
    /// </summary>
    [Range(0.0, 2.0)]
    public double NudgeGain { get; set; } = 0.5;

    /// <summary>Exponential half-life, in days, for a single thumb's contribution to
    /// <c>media_rotation.nudge</c> (SPEC F150.9: <c>0.5^(age_days / HalfLifeDays)</c>). Default 30,
    /// range 1–365 — SPEC F155.1's own explicit bound: 0 would divide-by-zero the exponent, past a
    /// year is longer than this catalog's own rotation horizon.</summary>
    [Range(1, 365)]
    public int HalfLifeDays { get; set; } = 30;

    /// <summary>Divisor that normalizes the age-decayed thumb sum into the nudge's clamped [-1, 1]
    /// range (SPEC F150.9). Default 5, range 1–100 — SPEC F155.1's own explicit bound: 0 would
    /// divide-by-zero.</summary>
    [Range(1, 100)]
    public int Saturation { get; set; } = 5;

    /// <summary>Per-IP cooldown, in seconds, on the <c>thumbs</c> route limiter chain (SPEC
    /// F150.5). Default 30. Range 1–3600 (F155.1 leaves this bound unstated, T357's own choice): a
    /// cooldown past an hour would read as the spectator control being broken, not throttled.</summary>
    [Range(1, 3600)]
    public int ThumbCooldownSeconds { get; set; } = 30;

    /// <summary>Per-IP AND per-listener daily cap on accepted thumb posts (SPEC F150.5). Default
    /// 60. Range 1–10,000 (F155.1 leaves this bound unstated, T357's own choice): generous enough
    /// for a household sharing one IP across a full day of continuous listening, without leaving
    /// the cap effectively unbounded.</summary>
    [Range(1, 10_000)]
    public int ThumbDailyCap { get; set; } = 60;

    /// <summary>Age, in days, past which <c>library.media_thumb</c> rows are swept by the
    /// gardener's hourly pass; the lifetime <c>thumbs_up</c>/<c>thumbs_down</c> counters and the
    /// computed nudge survive the sweep (SPEC F150.9). Default 90. Range 1–3650 (F155.1 leaves
    /// this bound unstated, T357's own choice — matches this same epic's F152.5 "days 1–3650"
    /// convention for its own rotation-adjacent day count).</summary>
    [Range(1, 3650)]
    public int ThumbRetentionDays { get; set; } = 90;

    /// <summary>Days since discovery, with zero plays recorded, before a playable row is flagged
    /// <c>shelf_dust</c> (SPEC F153.7). Default 90. Range 1–3650 (F155.1 leaves this bound
    /// unstated, T357's own choice — the same F152.5 "days 1–3650" convention as
    /// <see cref="ThumbRetentionDays"/> above).</summary>
    [Range(1, 3650)]
    public int ShelfDustDays { get; set; } = 90;

    /// <summary>Duration tolerance, in milliseconds, for the <c>near_duplicate</c> grouping
    /// function — anchored to the group's shortest member, never chained pairwise (SPEC F153.5).
    /// Default 2000. Range 0–60,000 (F155.1 leaves this bound unstated, T357's own choice): 0
    /// requires an exact-duration match; a full minute is already generous same-recording drift.</summary>
    [Range(0, 60_000)]
    public int DuplicateToleranceMs { get; set; } = 2000;

    /// <summary>How often <c>GardenerService</c>'s bounded-batch pass runs, in minutes (SPEC
    /// F153.2). Default 60. Range 1–1440 (F155.1 leaves this bound unstated, T357's own choice): a
    /// day is the loosest cadence that still calls this a gardener rather than a cron job nobody
    /// remembers exists.</summary>
    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Rows processed per bounded pass — the same <c>EnrichmentService</c> backfill shape
    /// (SPEC F153.2). Default 500. Range 1–10,000 (F155.1 leaves this bound unstated, T357's own
    /// choice): mirrors the order of magnitude of this codebase's other backfill batch sizes (e.g.
    /// <c>CueDetectionOptions.BackfillBatchSize</c>), scaled up for a per-row cost that is a
    /// metadata predicate check, not an ffmpeg/HTTP round-trip.
    ///
    /// <para>
    /// <b>As built at T372:</b> a predicate-based pass (<c>dead_file</c> today; <c>stale_metadata</c>,
    /// <c>shelf_dust</c>, <c>unreachable</c> later) reconciles set-based in ONE two-statement
    /// transaction with no batch concept at all (postgres-dba rule 7 — SQL does the set-based work,
    /// this knob has nothing to bound there). This value only ever governs an ITERATIVE pass — today
    /// only <c>near_duplicate</c>'s own grouping is expected to need one — the same
    /// <c>EnrichmentService</c> backfill shape the summary above already names.
    /// </para></summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 500;

    /// <summary>The one destructive-write gate (SPEC F154.2). Nested, not top-level — see
    /// <see cref="GardenerFileActionsOptions"/>'s own remarks for why nesting costs nothing here
    /// even though DataAnnotations validation does not recurse into it.</summary>
    public GardenerFileActionsOptions FileActions { get; set; } = new();
}
